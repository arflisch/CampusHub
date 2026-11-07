using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.UseCases.Common;
using FluentResults;

namespace Dev4u.CampusHub.Enrollment.UseCases.Enrollments;

/// <summary>
/// A student cancels a confirmed enrollment before the course starts: the money is refunded, the seat given back,
/// then the enrollment is cancelled (EnrollmentCancelled through the outbox). Refund and release are idempotent and
/// come first: if one of them fails, nothing is recorded and the student can simply try again.
/// </summary>
[Export, Scoped]
public sealed class CancelMyEnrollment(IEnrollmentRepository enrollments, ICoursePayments payments, ICourseSeats seats, TimeProvider timeProvider)
{
    public async Task<Result<EnrollmentDto>> ExecuteAsync(Guid enrollmentId, string studentId, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || !enrollment.BelongsTo(studentId))
        {
            return Result.Fail<EnrollmentDto>(EnrollmentErrors.NotFound(enrollmentId)); // never reveal someone else's enrollment
        }

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            return Result.Ok(enrollment.ToDto());
        }

        // Checked before any side effect: the domain checks again when cancelling.
        if (enrollment.Status != EnrollmentStatus.Confirmed)
        {
            return Result.Fail<EnrollmentDto>(EnrollmentErrors.NotCancellable(enrollmentId));
        }

        if (enrollment.CourseStartsAt is { } startsAt && timeProvider.GetUtcNow() >= startsAt)
        {
            return Result.Fail<EnrollmentDto>(EnrollmentErrors.CourseStarted(enrollmentId));
        }

        if (enrollment.RequiresPayment)
        {
            var refunded = await payments.RefundAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
            if (refunded.IsFailed)
            {
                return refunded.ToResult<EnrollmentDto>();
            }
        }

        if (enrollment.ReservationId is { } reservationId)
        {
            var released = await seats.ReleaseAsync(enrollment.CourseId, reservationId, cancellationToken).ConfigureAwait(false);
            if (released.IsFailed)
            {
                return released.ToResult<EnrollmentDto>();
            }
        }

        var cancelled = await ConcurrencyRetry.ExecuteAsync(async ct =>
        {
            var current = await enrollments.GetAsync(enrollmentId, ct).ConfigureAwait(false);
            if (current is null)
            {
                return Result.Fail(EnrollmentErrors.NotFound(enrollmentId));
            }

            var alreadyCancelled = current.Status == EnrollmentStatus.Cancelled;
            var result = current.Cancel(StudentEnrollment.CancelledByStudent, timeProvider.GetUtcNow());
            if (result.IsSuccess && !alreadyCancelled)
            {
                result = await enrollments.SaveAsync(current, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    EnrollmentMetrics.RecordCancelled(StudentEnrollment.CancelledByStudent);
                }
            }

            enrollment = current;
            return result;
        }, cancellationToken).ConfigureAwait(false);

        return cancelled.IsFailed ? cancelled.ToResult<EnrollmentDto>() : Result.Ok(enrollment.ToDto());
    }
}

/// <summary>
/// Reacts to CourseCancelled: every confirmed enrollment of the course is refunded and cancelled (fan-out). Enrollments
/// still in their saga are left to it: Catalog refuses to confirm a seat of a cancelled course, so the saga refunds
/// and rejects them itself. Idempotent: a redelivered event skips the enrollments already cancelled, so a transient
/// failure is retried by redelivering the whole event. Returns the number of enrollments cancelled now.
/// </summary>
[Export, Scoped]
public sealed class CancelEnrollmentsOfCancelledCourse(IEnrollmentRepository enrollments, ICoursePayments payments, TimeProvider timeProvider)
{
    public const int MaxEnrollmentsPerCourse = 1000;

    public async Task<Result<int>> ExecuteAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var ofCourse = await enrollments.ListForCourseAsync(courseId, MaxEnrollmentsPerCourse, cancellationToken).ConfigureAwait(false);
        var cancelledNow = 0;
        foreach (var enrollmentId in ofCourse.Where(e => e.Status == EnrollmentStatus.Confirmed).Select(e => e.Id))
        {
            var result = await ConcurrencyRetry.ExecuteAsync(async ct =>
            {
                var enrollment = await enrollments.GetAsync(enrollmentId, ct).ConfigureAwait(false);
                if (enrollment is not { Status: EnrollmentStatus.Confirmed })
                {
                    return Result.Ok(); // cancelled meanwhile
                }

                if (enrollment.RequiresPayment)
                {
                    var refunded = await payments.RefundAsync(enrollment.Id, ct).ConfigureAwait(false);
                    if (refunded.IsFailed)
                    {
                        return refunded;
                    }
                }

                var cancelled = enrollment.Cancel(StudentEnrollment.CancelledWithCourse, timeProvider.GetUtcNow());
                if (cancelled.IsFailed)
                {
                    return cancelled;
                }

                var saved = await enrollments.SaveAsync(enrollment, ct).ConfigureAwait(false);
                if (saved.IsSuccess)
                {
                    EnrollmentMetrics.RecordCancelled(StudentEnrollment.CancelledWithCourse);
                }

                return saved;
            }, cancellationToken).ConfigureAwait(false);

            if (result.IsFailed)
            {
                return result.ToResult<int>(); // the subscriber asks Dapr to redeliver: done ones are skipped next time
            }

            cancelledNow++;
        }

        return Result.Ok(cancelledNow);
    }
}

/// <summary>Somebody enrolled in one of my courses, as the teacher sees it.</summary>
public sealed record ParticipantDto(Guid EnrollmentId, string StudentId, string? StudentName, string Status,
                                    int CoursePriceInCents, DateTimeOffset RequestedAt);

/// <summary>
/// The participants of a course, for its teacher. The ownership is checked against the teacher copied on each
/// enrollment with the seat: a teacher asking for someone else's course simply gets nobody.
/// </summary>
[Export, Scoped]
public sealed class ListCourseParticipants(IEnrollmentRepository enrollments)
{
    public async Task<Result<IReadOnlyList<ParticipantDto>>> ExecuteAsync(Guid courseId, string teacherId, CancellationToken cancellationToken)
    {
        var ofCourse = await enrollments.ListForCourseAsync(courseId, CancelEnrollmentsOfCancelledCourse.MaxEnrollmentsPerCourse, cancellationToken)
                                        .ConfigureAwait(false);
        return Result.Ok<IReadOnlyList<ParticipantDto>>(
        [
            .. ofCourse.Where(e => string.Equals(e.CourseTeacherId, teacherId, StringComparison.Ordinal))
                       .Select(e => new ParticipantDto(e.Id, e.StudentId, e.StudentName, e.Status.ToString(), e.CoursePriceInCents, e.RequestedAt)),
        ]);
    }
}
