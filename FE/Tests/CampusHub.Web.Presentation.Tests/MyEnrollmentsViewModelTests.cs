using AwesomeAssertions;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.ViewModels;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dev4u.CampusHub.Web.Presentation.Tests;

public class MyEnrollmentsViewModelTests
{
    private readonly FakeEnrollmentService _enrollments = new();
    private readonly FakeTimeProvider _time = new();
    private readonly MyEnrollmentsViewModel _sut;
    private readonly Guid _id = Guid.NewGuid();

    private readonly FakeLiveUpdates _live = new();

    public MyEnrollmentsViewModelTests() => _sut = new MyEnrollmentsViewModel(_enrollments, _time, _live);

    [Fact]
    public async Task A_running_saga_is_polled_until_it_is_settled()
    {
        _enrollments.AnswerList(
            [TestData.Enrollment(_id, EnrollmentState.Requested)],
            [TestData.Enrollment(_id, EnrollmentState.SeatHeld)],
            [TestData.Enrollment(_id, EnrollmentState.Confirmed)]);

        await _sut.InitializeAsync(CancellationToken.None);
        _sut.IsPolling.Should().BeTrue();

        await TickAsync(expectedCalls: 2);
        _sut.Enrollments.Single().State.Should().Be(EnrollmentState.SeatHeld);

        await TickAsync(expectedCalls: 3);
        await _sut.PollingTask!;

        _sut.Enrollments.Single().State.Should().Be(EnrollmentState.Confirmed);
        _sut.IsPolling.Should().BeFalse();
        _time.Advance(MyEnrollmentsViewModel.PollInterval * 3);
        _enrollments.ListCalls.Should().Be(3, "polling stops once every enrollment is settled");
    }

    [Fact]
    public async Task Nothing_is_polled_when_every_enrollment_is_settled()
    {
        _enrollments.AnswerList([TestData.Enrollment(_id, EnrollmentState.Rejected)]);

        await _sut.InitializeAsync(CancellationToken.None);

        _sut.IsPolling.Should().BeFalse();
        _sut.PollingTask.Should().BeNull();
    }

    [Fact]
    public async Task A_transient_failure_does_not_stop_the_polling()
    {
        _enrollments.AnswerList([TestData.Enrollment(_id, EnrollmentState.Requested)]);
        _enrollments.AnswerListResult(TestData.Transient<IReadOnlyList<EnrollmentItem>>());
        _enrollments.AnswerList([TestData.Enrollment(_id, EnrollmentState.Confirmed)]);

        await _sut.InitializeAsync(CancellationToken.None);
        await TickAsync(expectedCalls: 2);
        await TickAsync(expectedCalls: 3);
        await _sut.PollingTask!;

        _sut.Enrollments.Single().State.Should().Be(EnrollmentState.Confirmed);
        _sut.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Leaving_the_page_stops_the_polling()
    {
        _enrollments.AnswerList([TestData.Enrollment(_id, EnrollmentState.Requested)]);
        await _sut.InitializeAsync(CancellationToken.None);
        var polling = _sut.PollingTask!;

        _sut.Deactivate();
        await polling;

        _sut.IsPolling.Should().BeFalse();
        _time.Advance(MyEnrollmentsViewModel.PollInterval * 3);
        _enrollments.ListCalls.Should().Be(1);
    }

    [Fact]
    public async Task A_confirmed_enrollment_can_be_cancelled_until_the_course_starts()
    {
        var confirmed = TestData.Enrollment(_id, EnrollmentState.Confirmed); // course starts 2026-11-02
        _enrollments.AnswerList([confirmed]);
        await _sut.InitializeAsync(CancellationToken.None);

        _sut.CancelCommand.CanExecute(confirmed).Should().BeTrue();
        await _sut.CancelCommand.ExecuteAsync(confirmed);

        _enrollments.Cancelled.Should().Equal(_id);
        _sut.Enrollments.Single().State.Should().Be(EnrollmentState.Cancelled);
        _sut.InfoMessage.Should().Contain("cancelled");

        _time.SetUtcNow(TestData.CourseStart);
        _sut.CancelCommand.CanExecute(confirmed).Should().BeFalse("the course has started");
    }

    [Fact]
    public async Task A_pushed_change_reloads_the_list_at_once()
    {
        _enrollments.AnswerList([TestData.Enrollment(_id, EnrollmentState.Requested)], [TestData.Enrollment(_id, EnrollmentState.SeatHeld)]);
        await _sut.InitializeAsync(CancellationToken.None);

        _live.Push(new EnrollmentChange(_id, Guid.NewGuid(), EnrollmentState.SeatHeld));
        await Task.Delay(50);

        _sut.Enrollments.Single().State.Should().Be(EnrollmentState.SeatHeld, "no need to wait for the next poll");
        _sut.Deactivate();
    }

    // The PeriodicTimer continuation runs on the thread pool: advance the clock, then wait for the poll to happen.
    private async Task TickAsync(int expectedCalls)
    {
        _time.Advance(MyEnrollmentsViewModel.PollInterval);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (_enrollments.ListCalls < expectedCalls)
        {
            DateTime.UtcNow.Should().BeBefore(deadline, $"poll #{expectedCalls} should have happened");
            await Task.Delay(10);
        }

        // Let the loop store the answer and wait for the next tick before the test advances the clock again.
        await Task.Delay(50);
    }
}
