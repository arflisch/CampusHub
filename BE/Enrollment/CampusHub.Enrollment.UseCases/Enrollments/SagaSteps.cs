using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.UseCases.Common;
using FluentResults;

namespace Dev4u.CampusHub.Enrollment.UseCases.Enrollments;

// The steps of the enrollment saga. The workflow (in the host) only orchestrates; the business decisions
// live here and are unit-testable without Dapr. Every step is idempotent because workflow activities run at
// least once and are retried by their policy.

/// <summary>Step 1: hold a seat in Catalog and remember it on the enrollment.</summary>
[Export, Scoped]
public sealed class ReserveSeatForEnrollment(IEnrollmentRepository enrollments, ICourseSeats seats, TimeProvider timeProvider)
{
    public Task<Result> ExecuteAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
        ConcurrencyRetry.ExecuteAsync(async ct =>
        {
            var enrollment = await enrollments.GetAsync(enrollmentId, ct).ConfigureAwait(false);
            if (enrollment is null)
            {
                return Result.Fail(EnrollmentErrors.NotFound(enrollmentId));
            }

            if (enrollment.Status is EnrollmentStatus.SeatHeld or EnrollmentStatus.Confirmed)
            {
                return Result.Ok(); // already done by a previous attempt
            }

            if (enrollment.Status == EnrollmentStatus.Rejected)
            {
                return Result.Fail(EnrollmentErrors.InvalidTransition(enrollment.Status, EnrollmentStatus.SeatHeld));
            }

            // Same idempotency key on every attempt: Catalog returns the seat it already gave.
            var hold = await seats.ReserveAsync(enrollment.CourseId, enrollment.StudentId, enrollment.SeatIdempotencyKey, ct)
                                  .ConfigureAwait(false);
            if (hold.IsFailed)
            {
                return hold.ToResult();
            }

            var marked = enrollment.MarkSeatReserved(hold.Value, timeProvider.GetUtcNow());
            return marked.IsFailed
                ? marked
                : await enrollments.SaveAsync(enrollment, ct).ConfigureAwait(false);
        }, cancellationToken);
}

/// <summary>
/// Step 2 (paid courses only): charge the student through Billing, then remember it on the enrollment. Billing is
/// idempotent on the enrollment id, so a retried step never charges twice; a declined card fails permanently with
/// <see cref="ICoursePayments.DeclinedCode"/>. A free course has nothing to pay: the step succeeds without a call.
/// </summary>
[Export, Scoped]
public sealed class PayForEnrollment(IEnrollmentRepository enrollments, ICoursePayments payments, TimeProvider timeProvider)
{
    public Task<Result> ExecuteAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
        ConcurrencyRetry.ExecuteAsync(async ct =>
        {
            var enrollment = await enrollments.GetAsync(enrollmentId, ct).ConfigureAwait(false);
            if (enrollment is null)
            {
                return Result.Fail(EnrollmentErrors.NotFound(enrollmentId));
            }

            if (enrollment.Status is EnrollmentStatus.Paid or EnrollmentStatus.Confirmed || !enrollment.RequiresPayment)
            {
                return Result.Ok(); // already done by a previous attempt, or nothing to pay
            }

            if (enrollment.Status != EnrollmentStatus.SeatHeld)
            {
                return Result.Fail(EnrollmentErrors.InvalidTransition(enrollment.Status, EnrollmentStatus.Paid));
            }

            var charged = await payments.ChargeAsync(enrollment.Id, enrollment.StudentId, enrollment.CourseId,
                                                     enrollment.CoursePriceInCents, ct).ConfigureAwait(false);
            if (charged.IsFailed)
            {
                return charged;
            }

            var paid = enrollment.MarkPaid(timeProvider.GetUtcNow());
            return paid.IsFailed
                ? paid
                : await enrollments.SaveAsync(enrollment, ct).ConfigureAwait(false);
        }, cancellationToken);
}

/// <summary>Compensation: give the money back through Billing. Safe to replay, and a no-op for a free course.</summary>
[Export, Scoped]
public sealed class RefundEnrollmentPayment(IEnrollmentRepository enrollments, ICoursePayments payments)
{
    public async Task<Result> ExecuteAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is not { RequiresPayment: true })
        {
            return Result.Ok();
        }

        // Called even when the enrollment is not marked Paid: the charge may have gone through at Billing while its
        // answer was lost. Billing refunds only what it actually took.
        return await payments.RefundAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Step 3 (pivot): confirm the seat in Catalog, then confirm the enrollment. The enrollment and its
/// EnrollmentConfirmed outbox message are written in one transaction by the repository.
/// </summary>
[Export, Scoped]
public sealed class ConfirmEnrollment(IEnrollmentRepository enrollments, ICourseSeats seats, TimeProvider timeProvider)
{
    public Task<Result> ExecuteAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
        ConcurrencyRetry.ExecuteAsync(async ct =>
        {
            var enrollment = await enrollments.GetAsync(enrollmentId, ct).ConfigureAwait(false);
            if (enrollment is null)
            {
                return Result.Fail(EnrollmentErrors.NotFound(enrollmentId));
            }

            if (enrollment.Status == EnrollmentStatus.Confirmed)
            {
                return Result.Ok(); // already done by a previous attempt
            }

            if (enrollment.Status is not (EnrollmentStatus.SeatHeld or EnrollmentStatus.Paid) || enrollment.ReservationId is not { } reservationId)
            {
                return Result.Fail(EnrollmentErrors.NoSeatReserved(enrollmentId));
            }

            if (enrollment.RequiresPayment && enrollment.Status != EnrollmentStatus.Paid)
            {
                return Result.Fail(EnrollmentErrors.InvalidTransition(enrollment.Status, EnrollmentStatus.Confirmed));
            }

            var seatConfirmed = await seats.ConfirmAsync(enrollment.CourseId, reservationId, ct).ConfigureAwait(false);
            if (seatConfirmed.IsFailed)
            {
                return seatConfirmed;
            }

            var confirmed = enrollment.Confirm(timeProvider.GetUtcNow());
            return confirmed.IsFailed
                ? confirmed
                : await enrollments.SaveAsync(enrollment, ct).ConfigureAwait(false);
        }, cancellationToken);
}

/// <summary>Final step of a failed saga: record why the enrollment was refused.</summary>
[Export, Scoped]
public sealed class RejectEnrollment(IEnrollmentRepository enrollments, TimeProvider timeProvider)
{
    public Task<Result> ExecuteAsync(Guid enrollmentId, string reasonCode, CancellationToken cancellationToken) =>
        ConcurrencyRetry.ExecuteAsync(async ct =>
        {
            var enrollment = await enrollments.GetAsync(enrollmentId, ct).ConfigureAwait(false);
            if (enrollment is null)
            {
                return Result.Fail(EnrollmentErrors.NotFound(enrollmentId));
            }

            var rejected = enrollment.Reject(reasonCode, timeProvider.GetUtcNow());
            return rejected.IsFailed
                ? rejected
                : await enrollments.SaveAsync(enrollment, ct).ConfigureAwait(false);
        }, cancellationToken);
}

/// <summary>Compensation: give the held seat back to Catalog. Safe to replay.</summary>
[Export, Scoped]
public sealed class ReleaseEnrollmentSeat(IEnrollmentRepository enrollments, ICourseSeats seats)
{
    public async Task<Result> ExecuteAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment?.ReservationId is not { } reservationId)
        {
            // No seat known: nothing to give back. A hold whose response was lost expires on its own in Catalog.
            return Result.Ok();
        }

        return await seats.ReleaseAsync(enrollment.CourseId, reservationId, cancellationToken).ConfigureAwait(false);
    }
}
