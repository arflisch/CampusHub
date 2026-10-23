using AwesomeAssertions;
using Dev4u.CampusHub.Billing.Domain.Payments;
using Dev4u.CampusHub.Billing.UseCases.Payments;
using Dev4u.CampusHub.Errors;
using FluentResults;
using Moq;
using Xunit;

namespace Dev4u.CampusHub.Billing.UseCases.Tests;

/// <summary>In-memory repository: the behaviour under test is the idempotency, which needs real state.</summary>
internal sealed class InMemoryPayments : IPaymentRepository
{
    public Dictionary<Guid, Payment> Items { get; } = [];

    public Task<Payment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.GetValueOrDefault(enrollmentId));

    public Task<bool> AddIfAbsentAsync(Payment payment, CancellationToken cancellationToken) =>
        Task.FromResult(Items.TryAdd(payment.EnrollmentId, payment));

    public Task<Result> SaveAsync(Payment payment, CancellationToken cancellationToken)
    {
        Items[payment.EnrollmentId] = payment;
        return Task.FromResult(Result.Ok());
    }

    public Task<IReadOnlyList<Payment>> ListForStudentAsync(string studentId, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payment>>([.. Items.Values.Where(p => p.StudentId == studentId)]);
}

public class ChargeEnrollmentTests
{
    private readonly InMemoryPayments _payments = new();
    private readonly Mock<IPaymentProvider> _provider = new();
    private readonly ChargeEnrollment _sut;
    private readonly ChargeEnrollmentCommand _command = new(Guid.NewGuid(), "student-1", Guid.NewGuid(), 4900);

    public ChargeEnrollmentTests() => _sut = new ChargeEnrollment(_payments, _provider.Object, TimeProvider.System);

    private void ProviderAnswers(ProviderCharge answer) =>
        _provider.Setup(p => p.ChargeAsync(_command.EnrollmentId, 4900, "EUR", It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok(answer));

    [Fact]
    public async Task A_retried_charge_returns_the_first_payment_without_charging_again()
    {
        ProviderAnswers(ProviderCharge.Success("ch_1"));

        var first = await _sut.ExecuteAsync(_command, CancellationToken.None);
        var retry = await _sut.ExecuteAsync(_command, CancellationToken.None);

        first.Value.Status.Should().Be("Captured");
        retry.Value.Should().Be(first.Value);
        _provider.Verify(p => p.ChargeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_declined_card_stays_declined_and_is_not_tried_again()
    {
        ProviderAnswers(ProviderCharge.Decline("card.limit_exceeded"));

        var first = await _sut.ExecuteAsync(_command, CancellationToken.None);
        var retry = await _sut.ExecuteAsync(_command, CancellationToken.None);

        first.Errors.HasCode(PaymentErrors.DeclinedCode).Should().BeTrue();
        first.Errors.AreTransient().Should().BeFalse("the saga must compensate, not retry");
        retry.Errors.HasCode(PaymentErrors.DeclinedCode).Should().BeTrue();
        _payments.Items[_command.EnrollmentId].DeclineReason.Should().Be("card.limit_exceeded");
        _provider.Verify(p => p.ChargeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task An_unreachable_provider_is_transient_and_stores_nothing()
    {
        _provider.Setup(p => p.ChargeAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Fail<ProviderCharge>(PaymentErrors.ProviderUnavailable()));

        var result = await _sut.ExecuteAsync(_command, CancellationToken.None);

        result.Errors.AreTransient().Should().BeTrue();
        _payments.Items.Should().BeEmpty();
    }
}

public class RefundEnrollmentTests
{
    private readonly InMemoryPayments _payments = new();
    private readonly Mock<IPaymentProvider> _provider = new();
    private readonly RefundEnrollment _sut;

    public RefundEnrollmentTests()
    {
        _provider.Setup(p => p.RefundAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _sut = new RefundEnrollment(_payments, _provider.Object, TimeProvider.System);
    }

    [Fact]
    public async Task A_captured_payment_is_refunded_once()
    {
        var payment = Payment.Captured(Guid.NewGuid(), "student-1", Guid.NewGuid(), 4900, "ch_1", DateTimeOffset.UtcNow).Value;
        _payments.Items[payment.EnrollmentId] = payment;

        (await _sut.ExecuteAsync(payment.EnrollmentId, CancellationToken.None)).IsSuccess.Should().BeTrue();
        (await _sut.ExecuteAsync(payment.EnrollmentId, CancellationToken.None)).IsSuccess.Should().BeTrue();

        payment.Status.Should().Be(PaymentStatus.Refunded);
        _provider.Verify(p => p.RefundAsync("ch_1", payment.EnrollmentId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Nothing_charged_means_nothing_to_refund()
    {
        var declined = Payment.Declined(Guid.NewGuid(), "student-1", Guid.NewGuid(), 4900, "card.limit_exceeded", DateTimeOffset.UtcNow).Value;
        _payments.Items[declined.EnrollmentId] = declined;

        (await _sut.ExecuteAsync(Guid.NewGuid(), CancellationToken.None)).IsSuccess.Should().BeTrue("no payment at all");
        (await _sut.ExecuteAsync(declined.EnrollmentId, CancellationToken.None)).IsSuccess.Should().BeTrue("a declined payment took nothing");

        _provider.Verify(p => p.RefundAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
