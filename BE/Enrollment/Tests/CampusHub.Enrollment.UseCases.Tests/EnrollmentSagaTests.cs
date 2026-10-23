using AwesomeAssertions;
using Dapr.Workflow;
using Dev4u.CampusHub.Enrollment.Host.Workflows;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.UseCases.Tests;

// The Dapr analyzer checks activity inputs; Moq matchers are typed object? on purpose here.
#pragma warning disable DAPR1303

/// <summary>
/// The orchestration logic of the saga, tested without Dapr: the workflow context is mocked, each activity
/// answer is scripted, and the test checks which compensations run.
/// </summary>
public class EnrollmentSagaTests
{
    private readonly Mock<WorkflowContext> _context = new();
    private readonly Guid _enrollmentId = Guid.NewGuid();
    private readonly List<string> _calls = [];

    public EnrollmentSagaTests()
    {
        _context.Setup(c => c.CreateReplaySafeLogger<EnrollmentSaga>()).Returns(NullLogger<EnrollmentSaga>.Instance);
        _context.Setup(c => c.InstanceId).Returns(EnrollmentSaga.InstanceIdFor(_enrollmentId));
    }

    private void Activity(string name, Func<SagaStepResult> answer) =>
        _context.Setup(c => c.CallActivityAsync<SagaStepResult>(name, It.IsAny<object?>(), It.IsAny<WorkflowTaskOptions?>()))
                .Returns(() =>
                {
                    _calls.Add(name);
                    return Task.FromResult(answer());
                });

    private Task<EnrollmentSagaOutcome> RunAsync() => new EnrollmentSaga().RunAsync(_context.Object, _enrollmentId);

    [Fact]
    public async Task Reserve_pay_then_confirm_completes_the_enrollment()
    {
        Activity(nameof(ReserveSeatActivity), SagaStepResult.Ok);
        Activity(nameof(PayForEnrollmentActivity), SagaStepResult.Ok);
        Activity(nameof(ConfirmEnrollmentActivity), SagaStepResult.Ok);

        var outcome = await RunAsync();

        outcome.Should().Be(EnrollmentSagaOutcome.Confirmed());
        _calls.Should().Equal(nameof(ReserveSeatActivity), nameof(PayForEnrollmentActivity), nameof(ConfirmEnrollmentActivity));
    }

    [Fact]
    public async Task A_declined_payment_compensates_and_never_reaches_the_pivot()
    {
        Activity(nameof(ReserveSeatActivity), SagaStepResult.Ok);
        Activity(nameof(PayForEnrollmentActivity), () => SagaStepResult.Failed("payment.declined"));
        Activity(nameof(RefundPaymentActivity), SagaStepResult.Ok);
        Activity(nameof(ReleaseSeatActivity), SagaStepResult.Ok);
        Activity(nameof(RejectEnrollmentActivity), SagaStepResult.Ok);

        var outcome = await RunAsync();

        outcome.Should().Be(EnrollmentSagaOutcome.Compensated("payment.declined"));
        _calls.Should().Equal(nameof(ReserveSeatActivity), nameof(PayForEnrollmentActivity),
                              nameof(RefundPaymentActivity), nameof(ReleaseSeatActivity), nameof(RejectEnrollmentActivity));
    }

    [Fact]
    public async Task Billing_unreachable_after_all_retries_refunds_just_in_case_then_compensates()
    {
        Activity(nameof(ReserveSeatActivity), SagaStepResult.Ok);
        _context.Setup(c => c.CallActivityAsync<SagaStepResult>(nameof(PayForEnrollmentActivity), It.IsAny<object?>(), It.IsAny<WorkflowTaskOptions?>()))
                .ThrowsAsync(new WorkflowTaskFailedException("retries exhausted", new WorkflowTaskFailureDetails("HttpRequestException", "connection refused")));
        Activity(nameof(RefundPaymentActivity), SagaStepResult.Ok);
        Activity(nameof(ReleaseSeatActivity), SagaStepResult.Ok);
        Activity(nameof(RejectEnrollmentActivity), SagaStepResult.Ok);

        var outcome = await RunAsync();

        outcome.Should().Be(EnrollmentSagaOutcome.Compensated("billing.unavailable"));
        _calls.Should().Equal(nameof(ReserveSeatActivity), nameof(RefundPaymentActivity), nameof(ReleaseSeatActivity),
                              nameof(RejectEnrollmentActivity));
    }

    [Fact]
    public async Task A_refused_reservation_rejects_without_compensation()
    {
        Activity(nameof(ReserveSeatActivity), () => SagaStepResult.Failed("course.full"));
        Activity(nameof(RejectEnrollmentActivity), SagaStepResult.Ok);

        var outcome = await RunAsync();

        outcome.Should().Be(EnrollmentSagaOutcome.Rejected("course.full"));
        _calls.Should().Equal(nameof(ReserveSeatActivity), nameof(RejectEnrollmentActivity));
    }

    [Fact]
    public async Task A_failed_pivot_refunds_releases_the_seat_then_rejects()
    {
        Activity(nameof(ReserveSeatActivity), SagaStepResult.Ok);
        Activity(nameof(PayForEnrollmentActivity), SagaStepResult.Ok);
        Activity(nameof(ConfirmEnrollmentActivity), () => SagaStepResult.Failed("reservation.expired"));
        Activity(nameof(RefundPaymentActivity), SagaStepResult.Ok);
        Activity(nameof(ReleaseSeatActivity), SagaStepResult.Ok);
        Activity(nameof(RejectEnrollmentActivity), SagaStepResult.Ok);

        var outcome = await RunAsync();

        outcome.Should().Be(EnrollmentSagaOutcome.Compensated("reservation.expired"));
        _calls.Should().Equal(nameof(ReserveSeatActivity), nameof(PayForEnrollmentActivity), nameof(ConfirmEnrollmentActivity),
                              nameof(RefundPaymentActivity), nameof(ReleaseSeatActivity), nameof(RejectEnrollmentActivity));
    }

    [Fact]
    public async Task Catalog_unreachable_after_all_retries_rejects_the_enrollment()
    {
        _context.Setup(c => c.CallActivityAsync<SagaStepResult>(nameof(ReserveSeatActivity), It.IsAny<object?>(), It.IsAny<WorkflowTaskOptions?>()))
                .ThrowsAsync(new WorkflowTaskFailedException("retries exhausted", new WorkflowTaskFailureDetails("HttpRequestException", "connection refused")));
        Activity(nameof(RejectEnrollmentActivity), SagaStepResult.Ok);

        var outcome = await RunAsync();

        outcome.Should().Be(EnrollmentSagaOutcome.Rejected("catalog.unavailable"));
        _context.Verify(c => c.CallActivityAsync<SagaStepResult>(nameof(RejectEnrollmentActivity),
            It.Is<object?>(input => input is RejectEnrollmentInput && ((RejectEnrollmentInput)input).ReasonCode == "catalog.unavailable"),
            It.IsAny<WorkflowTaskOptions?>()), Times.Once);
    }
}
