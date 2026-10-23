using FluentResults;

namespace Dev4u.CampusHub.Billing.Domain.Payments;

/// <summary>Port implemented by the MongoDB adapter. Saving is conditional on <see cref="Payment.Version"/>.</summary>
public interface IPaymentRepository
{
    Task<Payment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken);

    /// <summary>Stores a new payment; false when one already exists for the enrollment (concurrent charge).</summary>
    Task<bool> AddIfAbsentAsync(Payment payment, CancellationToken cancellationToken);

    Task<Result> SaveAsync(Payment payment, CancellationToken cancellationToken);

    Task<IReadOnlyList<Payment>> ListForStudentAsync(string studentId, int take, CancellationToken cancellationToken);
}

/// <summary>The provider's answer to a charge: accepted (with the charge id) or declined (with the reason).</summary>
public sealed record ProviderCharge(bool Accepted, string? Reference, string? DeclineReason)
{
    public static ProviderCharge Success(string reference) => new(true, reference, null);

    public static ProviderCharge Decline(string reason) => new(false, null, reason);
}

/// <summary>
/// Port to the payment provider (Stripe, Adyen... simulated in this sandbox). Both operations are idempotent on
/// the key they receive, like real providers: a retried call never charges or refunds twice. A declined card is an
/// answer (<see cref="ProviderCharge.Decline"/>); a failed result means the provider could not be reached (transient).
/// </summary>
public interface IPaymentProvider
{
    Task<Result<ProviderCharge>> ChargeAsync(Guid idempotencyKey, int amountInCents, string currency, CancellationToken cancellationToken);

    Task<Result> RefundAsync(string chargeReference, Guid idempotencyKey, CancellationToken cancellationToken);
}
