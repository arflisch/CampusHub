using AwesomeAssertions;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.UseCases.Enrollments;
using Dev4u.CampusHub.Errors;
using FluentResults;
using Moq;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.UseCases.Tests;

public class PaymentStepsTests
{
    private readonly Mock<IEnrollmentRepository> _repository = new();
    private readonly Mock<ICoursePayments> _payments = new();

    private StudentEnrollment SeatHeld(int priceInCents)
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = StudentEnrollment.Request(Guid.NewGuid(), Guid.NewGuid(), "student-1", now).Value;
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", now.AddDays(7), now.AddDays(37), priceInCents), now);
        _repository.Setup(r => r.GetAsync(enrollment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(enrollment);
        _repository.Setup(r => r.SaveAsync(enrollment, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        return enrollment;
    }

    private PayForEnrollment Pay => new(_repository.Object, _payments.Object, TimeProvider.System);

    [Fact]
    public async Task A_paid_course_is_charged_its_price_then_marked_paid()
    {
        var enrollment = SeatHeld(priceInCents: 4900);
        _payments.Setup(p => p.ChargeAsync(enrollment.Id, "student-1", enrollment.CourseId, 4900, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Ok());

        var result = await Pay.ExecuteAsync(enrollment.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        enrollment.Status.Should().Be(EnrollmentStatus.Paid);
    }

    [Fact]
    public async Task A_free_course_is_never_sent_to_Billing()
    {
        var enrollment = SeatHeld(priceInCents: 0);

        (await Pay.ExecuteAsync(enrollment.Id, CancellationToken.None)).IsSuccess.Should().BeTrue();

        enrollment.Status.Should().Be(EnrollmentStatus.SeatHeld, "the pivot comes next");
        _payments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task A_declined_card_is_a_permanent_failure_the_saga_compensates()
    {
        var enrollment = SeatHeld(priceInCents: 90000);
        _payments.Setup(p => p.ChargeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Fail(AppErrors.BusinessRule(ICoursePayments.DeclinedCode, "declined")));

        var result = await Pay.ExecuteAsync(enrollment.Id, CancellationToken.None);

        result.Errors.HasCode(ICoursePayments.DeclinedCode).Should().BeTrue();
        result.Errors.AreTransient().Should().BeFalse();
        enrollment.Status.Should().Be(EnrollmentStatus.SeatHeld);
    }

    [Fact]
    public async Task The_refund_is_asked_for_any_paid_course_even_if_the_charge_answer_was_lost()
    {
        var enrollment = SeatHeld(priceInCents: 4900); // not marked Paid: Billing may have taken the money anyway
        _payments.Setup(p => p.RefundAsync(enrollment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());

        var result = await new RefundEnrollmentPayment(_repository.Object, _payments.Object).ExecuteAsync(enrollment.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _payments.Verify(p => p.RefundAsync(enrollment.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_pivot_refuses_a_paid_course_that_was_not_paid()
    {
        var enrollment = SeatHeld(priceInCents: 4900);

        var result = await new ConfirmEnrollment(_repository.Object, Mock.Of<ICourseSeats>(), TimeProvider.System)
            .ExecuteAsync(enrollment.Id, CancellationToken.None);

        result.Errors.HasCode("enrollment.invalid_transition").Should().BeTrue();
    }
}
