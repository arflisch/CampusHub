using AwesomeAssertions;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Errors;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.Domain.Tests;

public class StudentEnrollmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private static StudentEnrollment Requested() =>
        StudentEnrollment.Request(Guid.NewGuid(), Guid.NewGuid(), "student-1", Now).Value;

    [Fact]
    public void A_request_needs_a_request_id_and_a_course()
    {
        StudentEnrollment.Request(Guid.Empty, Guid.NewGuid(), "student-1", Now).Errors.HasCode("enrollment.invalid_request").Should().BeTrue();
        StudentEnrollment.Request(Guid.NewGuid(), Guid.Empty, "student-1", Now).Errors.HasCode("enrollment.invalid_request").Should().BeTrue();
    }

    [Fact]
    public void The_happy_path_raises_exactly_one_EnrollmentConfirmed()
    {
        var enrollment = Requested();
        var reservationId = Guid.NewGuid();

        enrollment.MarkSeatReserved(new SeatHold(reservationId, "Intro to .NET", Now.AddDays(7), Now.AddDays(37)), Now).IsSuccess.Should().BeTrue();
        enrollment.Confirm(Now).IsSuccess.Should().BeTrue();
        enrollment.Confirm(Now.AddSeconds(1)).IsSuccess.Should().BeTrue(); // replayed activity

        enrollment.Status.Should().Be(EnrollmentStatus.Confirmed);
        var confirmed = enrollment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<EnrollmentConfirmed>().Subject;
        confirmed.EnrollmentId.Should().Be(enrollment.Id);
        confirmed.CourseTitle.Should().Be("Intro to .NET");
        confirmed.StudentId.Should().Be("student-1");
    }

    [Fact]
    public void Marking_the_same_seat_twice_is_harmless()
    {
        var enrollment = Requested();
        var reservationId = Guid.NewGuid();

        enrollment.MarkSeatReserved(new SeatHold(reservationId, "Intro", Now.AddDays(7), Now.AddDays(37)), Now).IsSuccess.Should().BeTrue();
        enrollment.MarkSeatReserved(new SeatHold(reservationId, "Intro", Now.AddDays(7), Now.AddDays(37)), Now).IsSuccess.Should().BeTrue();
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", Now.AddDays(7), Now.AddDays(37)), Now).Errors.HasCode("enrollment.invalid_transition").Should().BeTrue();
    }

    [Fact]
    public void Confirming_without_a_seat_is_refused()
    {
        var enrollment = Requested();

        enrollment.Confirm(Now).Errors.HasCode("enrollment.invalid_transition").Should().BeTrue();
        enrollment.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_confirmed_enrollment_can_no_longer_be_rejected()
    {
        var enrollment = Requested();
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", Now.AddDays(7), Now.AddDays(37)), Now);
        enrollment.Confirm(Now);

        enrollment.Reject("course.full", Now).Errors.HasCode("enrollment.invalid_transition").Should().BeTrue();
    }

    [Fact]
    public void Rejecting_is_idempotent_and_keeps_the_first_reason()
    {
        var enrollment = Requested();

        enrollment.Reject("course.full", Now).IsSuccess.Should().BeTrue();
        enrollment.Reject("catalog.unavailable", Now).IsSuccess.Should().BeTrue();

        enrollment.Status.Should().Be(EnrollmentStatus.Rejected);
        enrollment.RejectionCode.Should().Be("course.full");
    }

    private static StudentEnrollment Confirmed()
    {
        var enrollment = Requested();
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", Now.AddDays(1), Now.AddDays(30)), Now);
        enrollment.Confirm(Now);
        enrollment.ClearDomainEvents();
        return enrollment;
    }

    [Fact]
    public void The_confirmation_event_carries_the_course_dates()
    {
        var enrollment = Requested();
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", Now.AddDays(1), Now.AddDays(30)), Now);

        enrollment.Confirm(Now);

        var confirmed = enrollment.DomainEvents.OfType<EnrollmentConfirmed>().Single();
        confirmed.CourseStartsAt.Should().Be(Now.AddDays(1));
        confirmed.CourseEndsAt.Should().Be(Now.AddDays(30));
    }

    [Fact]
    public void A_confirmed_student_is_reminded_of_the_class_exactly_once()
    {
        var enrollment = Confirmed();

        enrollment.RemindOfClass(Now).IsSuccess.Should().BeTrue();
        enrollment.RemindOfClass(Now.AddHours(1)).IsSuccess.Should().BeTrue(); // next run of the job

        var reminder = enrollment.DomainEvents.OfType<ClassReminderDue>().Should().ContainSingle().Subject;
        reminder.CourseStartsAt.Should().Be(Now.AddDays(1));
        enrollment.ReminderSentAt.Should().Be(Now);
    }

    [Fact]
    public void Only_a_confirmed_enrollment_gets_a_reminder()
    {
        var enrollment = Requested();

        enrollment.RemindOfClass(Now).Errors.HasCode("enrollment.not_confirmed").Should().BeTrue();
        enrollment.DomainEvents.Should().BeEmpty();
    }
}
