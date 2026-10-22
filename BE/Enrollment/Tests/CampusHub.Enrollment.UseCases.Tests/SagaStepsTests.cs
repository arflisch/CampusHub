using AwesomeAssertions;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.UseCases.Enrollments;
using Dev4u.CampusHub.Errors;
using FluentResults;
using Moq;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.UseCases.Tests;

public class SagaStepsTests
{
    private readonly Mock<IEnrollmentRepository> _repository = new();
    private readonly Mock<ICourseSeats> _seats = new();
    private readonly StudentEnrollment _enrollment = StudentEnrollment.Request(Guid.NewGuid(), Guid.NewGuid(), "student-1", DateTimeOffset.UtcNow).Value;

    public SagaStepsTests()
    {
        _repository.Setup(r => r.GetAsync(_enrollment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_enrollment);
        _repository.Setup(r => r.SaveAsync(_enrollment, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
    }

    [Fact]
    public async Task Reserving_sends_a_stable_idempotency_key_and_remembers_the_seat()
    {
        var reservationId = Guid.NewGuid();
        _seats.Setup(s => s.ReserveAsync(_enrollment.CourseId, "student-1", $"enrollment:{_enrollment.Id}", It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Ok(new SeatHold(reservationId, "Intro to .NET")));

        var result = await new ReserveSeatForEnrollment(_repository.Object, _seats.Object, TimeProvider.System).ExecuteAsync(_enrollment.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _enrollment.Status.Should().Be(EnrollmentStatus.SeatHeld);
        _enrollment.ReservationId.Should().Be(reservationId);
    }

    [Fact]
    public async Task A_full_course_is_a_permanent_refusal()
    {
        _seats.Setup(s => s.ReserveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Fail<SeatHold>(AppErrors.Conflict("course.full", "full")));

        var result = await new ReserveSeatForEnrollment(_repository.Object, _seats.Object, TimeProvider.System).ExecuteAsync(_enrollment.Id, CancellationToken.None);

        result.Errors.HasCode("course.full").Should().BeTrue();
        result.Errors.AreTransient().Should().BeFalse();
        _repository.Verify(r => r.SaveAsync(It.IsAny<StudentEnrollment>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_concurrent_save_is_retried_with_a_fresh_copy()
    {
        var reservationId = Guid.NewGuid();
        _seats.Setup(s => s.ReserveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Result.Ok(new SeatHold(reservationId, "Intro")));
        _repository.SetupSequence(r => r.GetAsync(_enrollment.Id, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Copy(_enrollment))
                   .ReturnsAsync(Copy(_enrollment));
        _repository.SetupSequence(r => r.SaveAsync(It.IsAny<StudentEnrollment>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(Result.Fail(EnrollmentErrors.ConcurrencyConflict(_enrollment.Id)))
                   .ReturnsAsync(Result.Ok());

        var result = await new ReserveSeatForEnrollment(_repository.Object, _seats.Object, TimeProvider.System).ExecuteAsync(_enrollment.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _repository.Verify(r => r.SaveAsync(It.IsAny<StudentEnrollment>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Confirming_confirms_the_seat_then_saves_the_event()
    {
        var reservationId = Guid.NewGuid();
        _enrollment.MarkSeatReserved(reservationId, "Intro", DateTimeOffset.UtcNow);
        _seats.Setup(s => s.ConfirmAsync(_enrollment.CourseId, reservationId, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());

        var result = await new ConfirmEnrollment(_repository.Object, _seats.Object, TimeProvider.System).ExecuteAsync(_enrollment.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _enrollment.Status.Should().Be(EnrollmentStatus.Confirmed);
        _repository.Verify(r => r.SaveAsync(It.Is<StudentEnrollment>(e => e.DomainEvents.OfType<EnrollmentConfirmed>().Any()), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Releasing_without_a_known_seat_does_nothing()
    {
        var result = await new ReleaseEnrollmentSeat(_repository.Object, _seats.Object).ExecuteAsync(_enrollment.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _seats.VerifyNoOtherCalls();
    }

    private static StudentEnrollment Copy(StudentEnrollment e) =>
        StudentEnrollment.Rehydrate(e.Id, e.CourseId, e.StudentId, e.Status, e.ReservationId, e.CourseTitle, e.RejectionCode, e.RequestedAt, e.UpdatedAt, e.Version);
}
