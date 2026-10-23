using AwesomeAssertions;
using Dev4u.CampusHub.Billing.Domain.Payments;
using Dev4u.CampusHub.Errors;
using Xunit;

namespace Dev4u.CampusHub.Billing.Domain.Tests;

public class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(PaymentErrors.MaxAmountInCents + 1)]
    public void An_amount_out_of_range_is_refused_before_anything_is_charged(int amountInCents)
    {
        Payment.Validate(Guid.NewGuid(), "student-1", Guid.NewGuid(), amountInCents)
               .Errors.HasCode("payment.invalid_request").Should().BeTrue();
    }

    [Fact]
    public void Refunding_is_idempotent_and_does_nothing_for_a_declined_payment()
    {
        var captured = Payment.Captured(Guid.NewGuid(), "student-1", Guid.NewGuid(), 4900, "ch_1", Now).Value;
        var declined = Payment.Declined(Guid.NewGuid(), "student-1", Guid.NewGuid(), 4900, "card.limit_exceeded", Now).Value;

        captured.MarkRefunded(Now.AddMinutes(1));
        captured.MarkRefunded(Now.AddMinutes(2));
        declined.MarkRefunded(Now.AddMinutes(1));

        captured.Status.Should().Be(PaymentStatus.Refunded);
        captured.RefundedAt.Should().Be(Now.AddMinutes(1));
        declined.Status.Should().Be(PaymentStatus.Declined, "nothing was taken, nothing is given back");
    }
}
