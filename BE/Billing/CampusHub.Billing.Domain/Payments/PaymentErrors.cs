using Arc4u.Results;
using Dev4u.CampusHub.Errors;

namespace Dev4u.CampusHub.Billing.Domain.Payments;

/// <summary>Every failure the Billing domain can return, with its stable code.</summary>
public static class PaymentErrors
{
    public const string ConcurrencyConflictCode = "payment.concurrency_conflict";
    public const string DeclinedCode = "payment.declined";

    /// <summary>Highest amount Billing accepts to charge, in cents.</summary>
    public const int MaxAmountInCents = 1_000_000;

    public static ProblemDetailError InvalidRequest(string detail) =>
        AppErrors.Validation("payment.invalid_request", detail);

    /// <summary>The provider refused the charge (a business outcome, not a technical failure: never retried).</summary>
    public static ProblemDetailError Declined(Guid enrollmentId, string reason) =>
        AppErrors.BusinessRule(DeclinedCode, $"The payment for enrollment '{enrollmentId}' was declined ({reason}).");

    public static ProblemDetailError AlreadyRefunded(Guid enrollmentId) =>
        AppErrors.Conflict("payment.refunded", $"The payment for enrollment '{enrollmentId}' was refunded.");

    public static ProblemDetailError ProviderUnavailable() =>
        AppErrors.Unavailable("payment.provider_unavailable", "The payment provider could not be reached.");

    public static ProblemDetailError ConcurrencyConflict(Guid enrollmentId) =>
        AppErrors.Concurrency(ConcurrencyConflictCode, $"Payment '{enrollmentId}' was modified concurrently.");
}
