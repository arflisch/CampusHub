using AwesomeAssertions;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.UseCases.Enrollments;
using Dev4u.CampusHub.Errors;
using FluentResults;
using Moq;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.UseCases.Tests;

public class CancellationsTests
{
    private readonly Mock<IEnrollmentRepository> _repository = new();
    private readonly Mock<ICoursePayments> _payments = new();
    private readonly Mock<ICourseSeats> _seats = new();
    private readonly Guid _courseId = Guid.NewGuid();

    public CancellationsTests()
    {
        _payments.Setup(p => p.RefundAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _seats.Setup(s => s.ReleaseAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
        _repository.Setup(r => r.SaveAsync(It.IsAny<StudentEnrollment>(), It.IsAny<CancellationToken>())).ReturnsAsync(Result.Ok());
    }

    private StudentEnrollment Enrollment(EnrollmentStatus status, int priceInCents = 4900, string student = "student-1")
    {
        var now = DateTimeOffset.UtcNow;
        var enrollment = StudentEnrollment.Request(Guid.NewGuid(), _courseId, student, now, "Sam Student").Value;
        if (status != EnrollmentStatus.Requested)
        {
            enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", now.AddDays(7), now.AddDays(37), priceInCents, "teacher-1"), now);
            if (priceInCents > 0)
            {
                enrollment.MarkPaid(now);
            }

            if (status == EnrollmentStatus.Confirmed)
            {
                enrollment.Confirm(now);
            }
        }

        enrollment.ClearDomainEvents();
        _repository.Setup(r => r.GetAsync(enrollment.Id, It.IsAny<CancellationToken>())).ReturnsAsync(enrollment);
        return enrollment;
    }

    private CancelMyEnrollment CancelMine => new(_repository.Object, _payments.Object, _seats.Object, TimeProvider.System);

    [Fact]
    public async Task A_student_cancelling_is_refunded_and_gives_the_seat_back()
    {
        var enrollment = Enrollment(EnrollmentStatus.Confirmed);

        var result = await CancelMine.ExecuteAsync(enrollment.Id, "student-1", CancellationToken.None);

        result.Value.Status.Should().Be("Cancelled");
        _payments.Verify(p => p.RefundAsync(enrollment.Id, It.IsAny<CancellationToken>()), Times.Once);
        _seats.Verify(s => s.ReleaseAsync(_courseId, enrollment.ReservationId!.Value, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Nothing_is_cancelled_when_the_refund_fails()
    {
        var enrollment = Enrollment(EnrollmentStatus.Confirmed);
        _payments.Setup(p => p.RefundAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Result.Fail(AppErrors.Unavailable("billing.unavailable", "down")));

        var result = await CancelMine.ExecuteAsync(enrollment.Id, "student-1", CancellationToken.None);

        result.Errors.AreTransient().Should().BeTrue("the student can simply try again");
        enrollment.Status.Should().Be(EnrollmentStatus.Confirmed);
        _seats.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Someone_else_cannot_cancel_my_enrollment()
    {
        var enrollment = Enrollment(EnrollmentStatus.Confirmed);

        var result = await CancelMine.ExecuteAsync(enrollment.Id, "intruder", CancellationToken.None);

        result.Errors.HasCode("enrollment.not_found").Should().BeTrue();
        _payments.Verify(p => p.RefundAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_cancelled_course_refunds_and_cancels_its_confirmed_enrollments_only()
    {
        var confirmed = Enrollment(EnrollmentStatus.Confirmed);
        var inSaga = Enrollment(EnrollmentStatus.Requested);
        _repository.Setup(r => r.ListForCourseAsync(_courseId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync([confirmed, inSaga]);

        var result = await new CancelEnrollmentsOfCancelledCourse(_repository.Object, _payments.Object, TimeProvider.System)
            .ExecuteAsync(_courseId, CancellationToken.None);

        result.Value.Should().Be(1);
        confirmed.Status.Should().Be(EnrollmentStatus.Cancelled);
        confirmed.CancellationReason.Should().Be(StudentEnrollment.CancelledWithCourse);
        inSaga.Status.Should().Be(EnrollmentStatus.Requested, "its saga refunds and rejects it when Catalog refuses the seat");
    }

    [Fact]
    public async Task A_teacher_sees_the_participants_of_their_own_course_only()
    {
        var mine = Enrollment(EnrollmentStatus.Confirmed);
        _repository.Setup(r => r.ListForCourseAsync(_courseId, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([mine]);
        var sut = new ListCourseParticipants(_repository.Object);

        var forTeacher = await sut.ExecuteAsync(_courseId, "teacher-1", CancellationToken.None);
        var forAnother = await sut.ExecuteAsync(_courseId, "teacher-2", CancellationToken.None);

        forTeacher.Value.Should().ContainSingle().Which.StudentName.Should().Be("Sam Student");
        forAnother.Value.Should().BeEmpty();
    }
}
