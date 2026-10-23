using AwesomeAssertions;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.UseCases.Enrollments;
using Dev4u.CampusHub.Errors;
using FluentResults;
using Moq;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.UseCases.Tests;

public class RecoverStalledEnrollmentsTests
{
    private readonly Mock<IEnrollmentRepository> _repository = new();
    private readonly Mock<IEnrollmentSaga> _saga = new();
    private readonly Mock<ICourseSeats> _seats = new();
    private readonly Mock<ICoursePayments> _payments = new();
    private readonly RecoverStalledEnrollments _sut;

    public RecoverStalledEnrollmentsTests()
    {
        _repository.Setup(r => r.SaveAsync(It.IsAny<StudentEnrollment>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _seats.Setup(s => s.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _payments.Setup(p => p.RefundAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _sut = new RecoverStalledEnrollments(_repository.Object, _saga.Object,
                                             new RefundEnrollmentPayment(_repository.Object, _payments.Object),
                                             new ReleaseEnrollmentSeat(_repository.Object, _seats.Object),
                                             new RejectEnrollment(_repository.Object, TimeProvider.System),
                                             TimeProvider.System);
    }

    private StudentEnrollment Stalled(EnrollmentSagaState? sagaState, bool seatHeld)
    {
        var enrollment = StudentEnrollment.Request(Guid.NewGuid(), Guid.NewGuid(), "student-1", DateTimeOffset.UtcNow.AddHours(-1)).Value;
        if (seatHeld)
        {
            enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", DateTimeOffset.UtcNow.AddDays(7), DateTimeOffset.UtcNow.AddDays(37)),
                                        DateTimeOffset.UtcNow.AddHours(-1));
        }

        _repository.Setup(r => r.GetAsync(enrollment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(enrollment);
        _saga.Setup(s => s.GetStateAsync(enrollment.Id, It.IsAny<CancellationToken>()))
             .ReturnsAsync(sagaState is { } state
                 ? Result.Ok(state)
                 : Result.Fail<EnrollmentSagaState>(EnrollmentErrors.SagaUnavailable(enrollment.Id)));
        return enrollment;
    }

    [Fact]
    public async Task A_dead_saga_gives_the_seat_back_and_the_enrollment_expires_while_a_running_one_is_left_alone()
    {
        var dead = Stalled(EnrollmentSagaState.Ended, seatHeld: true);
        var running = Stalled(EnrollmentSagaState.Running, seatHeld: true);
        var unknown = Stalled(sagaState: null, seatHeld: false); // workflow engine unreachable
        _repository.Setup(r => r.ListStalledAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([dead, running, unknown]);

        var report = await _sut.ExecuteAsync(CancellationToken.None);

        report.Should().Be(new MaintenanceReport(Examined: 3, Handled: 1, Skipped: 1, Failed: 1));
        dead.Status.Should().Be(EnrollmentStatus.Rejected);
        dead.RejectionCode.Should().Be(RecoverStalledEnrollments.ExpiredReason);
        _seats.Verify(s => s.ReleaseAsync(dead.CourseId, dead.ReservationId!.Value, It.IsAny<CancellationToken>()), Times.Once);
        running.Status.Should().Be(EnrollmentStatus.SeatHeld);
        unknown.Status.Should().Be(EnrollmentStatus.Requested, "nothing is decided blindly when the saga state is unknown");
    }

    [Fact]
    public async Task Only_enrollments_untouched_for_longer_than_a_seat_hold_are_examined()
    {
        _repository.Setup(r => r.ListStalledAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([]);

        await _sut.ExecuteAsync(CancellationToken.None);

        _repository.Verify(r => r.ListStalledAsync(
            It.Is<DateTimeOffset>(limit => limit < DateTimeOffset.UtcNow.AddMinutes(-15)), It.IsAny<int>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_seat_that_cannot_be_released_keeps_the_enrollment_for_the_next_run()
    {
        var dead = Stalled(EnrollmentSagaState.NotFound, seatHeld: true);
        _repository.Setup(r => r.ListStalledAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([dead]);
        _seats.Setup(s => s.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Fail(AppErrors.Unavailable("catalog.unavailable", "down")));

        var report = await _sut.ExecuteAsync(CancellationToken.None);

        report.Failed.Should().Be(1);
        dead.Status.Should().Be(EnrollmentStatus.SeatHeld, "rejecting first would leave the seat taken for nothing");
    }
}

public class SendClassRemindersTests
{
    private readonly Mock<IEnrollmentRepository> _repository = new();
    private readonly SendClassReminders _sut;

    public SendClassRemindersTests() => _sut = new SendClassReminders(_repository.Object, TimeProvider.System);

    private StudentEnrollment ConfirmedStartingIn(TimeSpan delay)
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = StudentEnrollment.Request(Guid.NewGuid(), Guid.NewGuid(), "student-1", now).Value;
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", now + delay, now + delay + TimeSpan.FromDays(30)), now);
        enrollment.Confirm(now);
        enrollment.ClearDomainEvents();
        _repository.Setup(r => r.GetAsync(enrollment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(enrollment);
        return enrollment;
    }

    [Fact]
    public async Task Students_whose_course_starts_within_a_day_are_reminded_through_the_outbox()
    {
        var due = ConfirmedStartingIn(TimeSpan.FromHours(10));
        _repository.Setup(r => r.ListDueForReminderAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([due]);
        _repository.Setup(r => r.SaveAsync(due, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());

        var report = await _sut.ExecuteAsync(CancellationToken.None);

        report.Handled.Should().Be(1);
        due.DomainEvents.OfType<ClassReminderDue>().Should().ContainSingle();
        _repository.Verify(r => r.ListDueForReminderAsync(
            It.IsAny<DateTimeOffset>(),
            It.Is<DateTimeOffset>(to => to > DateTimeOffset.UtcNow.AddHours(23) && to <= DateTimeOffset.UtcNow.AddHours(24)),
            It.IsAny<int>(), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task A_reminder_that_cannot_be_saved_is_counted_and_left_for_the_next_run()
    {
        var due = ConfirmedStartingIn(TimeSpan.FromHours(10));
        _repository.Setup(r => r.ListDueForReminderAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([due]);
        _repository.Setup(r => r.SaveAsync(due, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Fail(EnrollmentErrors.ConcurrencyConflict(due.Id)));

        var report = await _sut.ExecuteAsync(CancellationToken.None);

        report.Should().Be(new MaintenanceReport(Examined: 1, Handled: 0, Skipped: 0, Failed: 1));
        _repository.Verify(r => r.GetAsync(due.Id, It.IsAny<CancellationToken>()), Times.Exactly(3), "a concurrency conflict reloads and retries");
    }
}
