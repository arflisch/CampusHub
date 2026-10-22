using Dapr.Workflow;

namespace Dev4u.CampusHub.Enrollment.Host.Workflows;

/// <summary>Result of one saga step. Serializable on purpose: it crosses the workflow boundary and is stored in its history.</summary>
internal sealed record SagaStepResult(bool Succeeded, string? ErrorCode)
{
    public static SagaStepResult Ok() => new(true, null);

    public static SagaStepResult Failed(string errorCode) => new(false, errorCode);
}

internal sealed record RejectEnrollmentInput(Guid EnrollmentId, string ReasonCode);

internal sealed record EnrollmentSagaOutcome(string Status, string? ReasonCode)
{
    public static EnrollmentSagaOutcome Confirmed() => new("Confirmed", null);

    public static EnrollmentSagaOutcome Rejected(string reasonCode) => new("Rejected", reasonCode);

    public static EnrollmentSagaOutcome Compensated(string reasonCode) => new("Compensated", reasonCode);
}

/// <summary>
/// Orchestrated saga: reserve a seat in Catalog → confirm the seat and the enrollment (pivot, publishes
/// EnrollmentConfirmed through the outbox). If the pivot fails, the seat is released and the enrollment rejected.
/// <para>
/// Workflow code is replayed from its history after every await, so it stays deterministic: no I/O, no clock,
/// no Guid.NewGuid here - every side effect is an activity. Activities run at least once and are retried with
/// <see cref="Retry"/>; they throw only for transient failures and report business refusals as results.
/// </para>
/// </summary>
internal sealed class EnrollmentSaga : Workflow<Guid, EnrollmentSagaOutcome>
{
    internal static readonly WorkflowTaskOptions Retry = new(new WorkflowRetryPolicy(
        maxNumberOfAttempts: 5,
        firstRetryInterval: TimeSpan.FromSeconds(2),
        backoffCoefficient: 2.0,
        maxRetryInterval: TimeSpan.FromSeconds(30)));

    /// <summary>One saga per enrollment: a second start for the same enrollment is a no-op.</summary>
    public static string InstanceIdFor(Guid enrollmentId) => $"enrollment-{enrollmentId:N}";

    public override async Task<EnrollmentSagaOutcome> RunAsync(WorkflowContext context, Guid input)
    {
        ArgumentNullException.ThrowIfNull(context);
        var logger = context.CreateReplaySafeLogger<EnrollmentSaga>();
        var enrollmentId = input;

        // 1. Hold a seat. A refusal (course full, closed...) ends the saga: nothing to compensate yet.
        SagaStepResult reserved;
        try
        {
            reserved = await context.CallActivityAsync<SagaStepResult>(nameof(ReserveSeatActivity), enrollmentId, Retry).ConfigureAwait(true);
        }
        catch (WorkflowTaskFailedException ex)
        {
            // Catalog stayed unreachable through every retry. A seat held meanwhile expires on its own.
            logger.LogSagaStepFailed(ex, enrollmentId, nameof(ReserveSeatActivity));
            reserved = SagaStepResult.Failed("catalog.unavailable");
        }

        if (!reserved.Succeeded)
        {
            var reason = reserved.ErrorCode ?? "enrollment.refused";
            await context.CallActivityAsync<SagaStepResult>(nameof(RejectEnrollmentActivity), new RejectEnrollmentInput(enrollmentId, reason), Retry).ConfigureAwait(true);
            return EnrollmentSagaOutcome.Rejected(reason);
        }

        // 2. Pivot: confirm the seat, then the enrollment (outbox). After this point the saga only moves forward.
        string failure;
        try
        {
            var confirmed = await context.CallActivityAsync<SagaStepResult>(nameof(ConfirmEnrollmentActivity), enrollmentId, Retry).ConfigureAwait(true);
            if (confirmed.Succeeded)
            {
                return EnrollmentSagaOutcome.Confirmed();
            }

            failure = confirmed.ErrorCode ?? "enrollment.confirmation_refused";
        }
        catch (WorkflowTaskFailedException ex)
        {
            logger.LogSagaStepFailed(ex, enrollmentId, nameof(ConfirmEnrollmentActivity));
            failure = "enrollment.confirmation_failed";
        }

        // 3. Compensation: give the seat back, then record the rejection. Both are idempotent.
        await context.CallActivityAsync<SagaStepResult>(nameof(ReleaseSeatActivity), enrollmentId, Retry).ConfigureAwait(true);
        await context.CallActivityAsync<SagaStepResult>(nameof(RejectEnrollmentActivity), new RejectEnrollmentInput(enrollmentId, failure), Retry).ConfigureAwait(true);
        return EnrollmentSagaOutcome.Compensated(failure);
    }
}
