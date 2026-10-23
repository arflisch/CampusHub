using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Billing.Domain.Payments;
using FluentResults;
using Microsoft.Extensions.Options;

namespace Dev4u.CampusHub.Billing.Infrastructure.Payments;

/// <summary>Settings of the simulated provider (section <c>Billing:FakeProvider</c>).</summary>
public sealed class FakePaymentProviderOptions
{
    public const string SectionName = "Billing:FakeProvider";

    /// <summary>
    /// Every student's card is accepted up to this amount, declined above it: create a course above the limit to
    /// see the saga compensate (seat given back, enrollment rejected).
    /// </summary>
    public int CardLimitInCents { get; set; } = 50_000;
}

/// <summary>
/// Stand-in for a real payment provider, behind the same port a Stripe or Adyen adapter would implement. It is
/// deterministic, like a provider's test mode: the decision depends only on the amount, and the charge reference
/// only on the idempotency key, so a retried call gets the same answer.
/// </summary>
[Export(typeof(IPaymentProvider)), Shared]
public sealed class FakePaymentProvider(IOptions<FakePaymentProviderOptions> options) : IPaymentProvider
{
    public const string LimitExceeded = "card.limit_exceeded";

    public Task<Result<ProviderCharge>> ChargeAsync(Guid idempotencyKey, int amountInCents, string currency, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var answer = amountInCents > options.Value.CardLimitInCents
            ? ProviderCharge.Decline(LimitExceeded)
            : ProviderCharge.Success($"fake_ch_{idempotencyKey:N}");
        return Task.FromResult(Result.Ok(answer));
    }

    public Task<Result> RefundAsync(string chargeReference, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Ok());
    }
}
