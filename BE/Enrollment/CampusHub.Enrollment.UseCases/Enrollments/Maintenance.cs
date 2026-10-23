using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.UseCases.Common;
using FluentResults;

namespace Dev4u.CampusHub.Enrollment.UseCases.Enrollments;

// Use cases run by the Hangfire jobs. Each run handles a bounded batch and never throws for one bad enrollment:
// what fails is counted, left as it is and picked up again by the next run.

/// <summary>What a maintenance run did, logged by the job.</summary>
/// <param name="Examined">Enrollments the run looked at.</param>
/// <param name="Handled">Enrollments it changed.</param>
/// <param name="Skipped">Enrollments it deliberately left alone.</param>
/// <param name="Failed">Enrollments it could not handle this time.</param>
public sealed record MaintenanceReport(int Examined, int Handled, int Skipped, int Failed);

/// <summary>
/// Settles the enrollments whose saga died (workflow failed, terminated, or never started) before the pivot: the
/// payment is refunded, the held seat is given back to Catalog and the enrollment is rejected as expired, so the
/// student sees a final answer instead of a request that never ends. An enrollment whose saga is still running is left to it.
/// </summary>
[Export, Scoped]
public sealed class RecoverStalledEnrollments(
    IEnrollmentRepository enrollments,
    IEnrollmentSaga saga,
    RefundEnrollmentPayment refund,
    ReleaseEnrollmentSeat releaseSeat,
    RejectEnrollment reject,
    TimeProvider timeProvider)
{
    public const string ExpiredReason = "reservation.expired";

    public async Task<MaintenanceReport> ExecuteAsync(CancellationToken cancellationToken)
    {
        var stalled = await enrollments.ListStalledAsync(timeProvider.GetUtcNow() - Constants.StalledAfter,
                                                         Constants.MaintenanceBatchSize, cancellationToken).ConfigureAwait(false);
        var (recovered, running, failed) = (0, 0, 0);
        foreach (var enrollmentId in stalled.Select(e => e.Id))
        {
            var state = await saga.GetStateAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
            if (state.IsFailed)
            {
                failed++; // workflow engine unreachable: decide nothing blindly
                continue;
            }

            if (state.Value == EnrollmentSagaState.Running)
            {
                running++;
                continue;
            }

            // Same steps, in the same order, as the saga's own compensation; all are idempotent.
            var settled = await refund.ExecuteAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
            if (settled.IsSuccess)
            {
                settled = await releaseSeat.ExecuteAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
            }

            if (settled.IsSuccess)
            {
                settled = await reject.ExecuteAsync(enrollmentId, ExpiredReason, cancellationToken).ConfigureAwait(false);
            }

            if (settled.IsSuccess)
            {
                recovered++;
            }
            else
            {
                failed++;
            }
        }

        return new MaintenanceReport(stalled.Count, recovered, running, failed);
    }
}

/// <summary>
/// Reminds the students whose course starts within <see cref="Constants.ReminderLeadTime"/>. Each reminder is a
/// <see cref="ClassReminderDue"/> event saved with the enrollment through the outbox, once per enrollment.
/// </summary>
[Export, Scoped]
public sealed class SendClassReminders(IEnrollmentRepository enrollments, TimeProvider timeProvider)
{
    public async Task<MaintenanceReport> ExecuteAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var due = await enrollments.ListDueForReminderAsync(now, now + Constants.ReminderLeadTime, Constants.MaintenanceBatchSize,
                                                            cancellationToken).ConfigureAwait(false);
        var (reminded, failed) = (0, 0);
        foreach (var enrollmentId in due.Select(e => e.Id))
        {
            var result = await ConcurrencyRetry.ExecuteAsync(async ct =>
            {
                // Reloaded on every attempt: a concurrent change must not be overwritten.
                var enrollment = await enrollments.GetAsync(enrollmentId, ct).ConfigureAwait(false);
                if (enrollment is null)
                {
                    return Result.Fail(EnrollmentErrors.NotFound(enrollmentId));
                }

                var remindedNow = enrollment.RemindOfClass(timeProvider.GetUtcNow());
                return remindedNow.IsFailed
                    ? remindedNow
                    : await enrollments.SaveAsync(enrollment, ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                reminded++;
            }
            else
            {
                failed++;
            }
        }

        return new MaintenanceReport(due.Count, reminded, Skipped: 0, failed);
    }
}
