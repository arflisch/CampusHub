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
/// Orchestrated saga: reserve a seat in Catalog → pay through Billing (paid courses only) → confirm the seat and the
/// enrollment (pivot, publishes EnrollmentConfirmed through the outbox). If the payment or the pivot fails, the
/// steps already done are compensated in reverse order: refund, release the seat, reject the enrollment.
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
        //    Catalog unreachable through every retry: a seat held meanwhile expires on its own.
        var notReserved = await RunStepAsync(context, logger, nameof(ReserveSeatActivity), enrollmentId, "catalog.unavailable").ConfigureAwait(true);
        if (notReserved is not null)
        {
            await context.CallActivityAsync<SagaStepResult>(nameof(RejectEnrollmentActivity), new RejectEnrollmentInput(enrollmentId, notReserved), Retry).ConfigureAwait(true);
            return EnrollmentSagaOutcome.Rejected(notReserved);
        }

        // 2. Pay (a free course passes straight through). A declined card is a business refusal: payment.declined.
        // 3. Pivot: confirm the seat, then the enrollment (outbox). After this point the saga only moves forward.
        var failure = await RunStepAsync(context, logger, nameof(PayForEnrollmentActivity), enrollmentId, "billing.unavailable").ConfigureAwait(true)
                      ?? await RunStepAsync(context, logger, nameof(ConfirmEnrollmentActivity), enrollmentId, "enrollment.confirmation_failed").ConfigureAwait(true);
        if (failure is null)
        {
            return EnrollmentSagaOutcome.Confirmed();
        }

        // Compensation, in reverse order. All steps are idempotent, and the refund is attempted even when the
        // payment step failed: its answer may have been lost after Billing took the money.
        await context.CallActivityAsync<SagaStepResult>(nameof(RefundPaymentActivity), enrollmentId, Retry).ConfigureAwait(true);
        await context.CallActivityAsync<SagaStepResult>(nameof(ReleaseSeatActivity), enrollmentId, Retry).ConfigureAwait(true);
        await context.CallActivityAsync<SagaStepResult>(nameof(RejectEnrollmentActivity), new RejectEnrollmentInput(enrollmentId, failure), Retry).ConfigureAwait(true);
        return EnrollmentSagaOutcome.Compensated(failure);
    }

    /// <summary>Runs one forward step: null when it succeeded, otherwise the code of the failure.</summary>
    private static async Task<string?> RunStepAsync(WorkflowContext context, ILogger logger, string activity, Guid enrollmentId,
                                                    string unavailableCode)
    {
        try
        {
            var result = await context.CallActivityAsync<SagaStepResult>(activity, enrollmentId, Retry).ConfigureAwait(true);
            return result.Succeeded ? null : result.ErrorCode ?? $"{activity}.refused";
        }
        catch (WorkflowTaskFailedException ex)
        {
            // Still failing transiently after every retry.
            logger.LogSagaStepFailed(ex, enrollmentId, activity);
            return unavailableCode;
        }
    }
}
