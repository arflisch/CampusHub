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
        var confirmed = enrollment.DomainEvents.OfType<EnrollmentConfirmed>().Should().ContainSingle().Subject;
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
        enrollment.DomainEvents.Where(e => e is not EnrollmentStatusChanged).Should().BeEmpty();
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
        enrollment.DomainEvents.Where(e => e is not EnrollmentStatusChanged).Should().BeEmpty();
    }

    [Fact]
    public void A_paid_course_must_be_paid_before_the_pivot()
    {
        var enrollment = Requested();
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", Now.AddDays(1), Now.AddDays(30), CoursePriceInCents: 4900), Now);

        enrollment.Confirm(Now).Errors.HasCode("enrollment.invalid_transition").Should().BeTrue("not paid yet");
        enrollment.MarkPaid(Now).IsSuccess.Should().BeTrue();
        enrollment.MarkPaid(Now).IsSuccess.Should().BeTrue(); // replayed activity
        enrollment.Confirm(Now).IsSuccess.Should().BeTrue();

        enrollment.Status.Should().Be(EnrollmentStatus.Confirmed);
    }

    [Fact]
    public void A_free_course_has_no_payment_step()
    {
        var enrollment = Requested();
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", Now.AddDays(1), Now.AddDays(30)), Now);

        enrollment.RequiresPayment.Should().BeFalse();
        enrollment.MarkPaid(Now).Errors.HasCode("enrollment.invalid_transition").Should().BeTrue();
        enrollment.Confirm(Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_paid_enrollment_can_still_be_compensated()
    {
        var enrollment = Requested();
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", Now.AddDays(1), Now.AddDays(30), CoursePriceInCents: 4900), Now);
        enrollment.MarkPaid(Now);

        enrollment.Reject("enrollment.confirmation_failed", Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Each_status_change_is_announced_once()
    {
        var enrollment = Requested();
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro", Now.AddDays(1), Now.AddDays(30), CoursePriceInCents: 4900), Now);
        enrollment.MarkPaid(Now);
        enrollment.MarkPaid(Now); // replayed activity
        enrollment.Confirm(Now);

        enrollment.DomainEvents.OfType<EnrollmentStatusChanged>().Select(e => e.Status).Should().Equal(
            EnrollmentStatus.Requested, EnrollmentStatus.SeatHeld, EnrollmentStatus.Paid, EnrollmentStatus.Confirmed);
    }

    [Fact]
    public void A_student_cancels_a_confirmed_enrollment_before_the_course_starts()
    {
        var enrollment = Confirmed(); // starts the day after Now

        enrollment.Cancel(StudentEnrollment.CancelledByStudent, Now).IsSuccess.Should().BeTrue();
        enrollment.Cancel(StudentEnrollment.CancelledByStudent, Now).IsSuccess.Should().BeTrue(); // twice: one event

        enrollment.Status.Should().Be(EnrollmentStatus.Cancelled);
        enrollment.DomainEvents.OfType<EnrollmentCancelled>().Should().ContainSingle()
                  .Which.Reason.Should().Be(StudentEnrollment.CancelledByStudent);
    }

    [Fact]
    public void A_student_cannot_cancel_once_the_course_started_but_a_cancelled_course_cancels_anyway()
    {
        var byStudent = Confirmed();
        var withCourse = Confirmed();

        byStudent.Cancel(StudentEnrollment.CancelledByStudent, Now.AddDays(2)).Errors.HasCode("enrollment.course_started").Should().BeTrue();
        withCourse.Cancel(StudentEnrollment.CancelledWithCourse, Now.AddDays(2)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Only_a_confirmed_enrollment_can_be_cancelled()
    {
        Requested().Cancel(StudentEnrollment.CancelledByStudent, Now).Errors.HasCode("enrollment.not_cancellable").Should().BeTrue();
    }
}
