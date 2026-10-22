using AwesomeAssertions;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using Dev4u.CampusHub.Errors;
using Xunit;

namespace Dev4u.CampusHub.Catalog.Domain.Tests;

public class CourseTests
{
    private const string Teacher = "teacher-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Hold = TimeSpan.FromMinutes(15);

    private static Course PublishedCourse(int capacity = 2)
    {
        var course = Course.Create(Guid.NewGuid(), "dotnet-101", "Intro to .NET", capacity, Teacher, Now).Value;
        course.Publish(Teacher, Now).IsSuccess.Should().BeTrue();
        return course;
    }

    [Fact]
    public void Create_normalizes_the_code_and_starts_in_draft()
    {
        var result = Course.Create(Guid.NewGuid(), " dotnet-101 ", " Intro ", 30, Teacher, Now);

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Should().Be("DOTNET-101");
        result.Value.Title.Should().Be("Intro");
        result.Value.Status.Should().Be(CourseStatus.Draft);
    }

    [Fact]
    public void Create_reports_every_invalid_field_at_once()
    {
        var result = Course.Create(Guid.NewGuid(), "x", "", 0, Teacher, Now);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().HaveCount(3);
        result.Errors.HasCode("course.invalid_code").Should().BeTrue();
        result.Errors.HasCode("course.invalid_title").Should().BeTrue();
        result.Errors.HasCode("course.invalid_capacity").Should().BeTrue();
    }

    [Fact]
    public void Only_the_teacher_can_publish()
    {
        var course = Course.Create(Guid.NewGuid(), "dotnet-101", "Intro", 10, Teacher, Now).Value;

        var result = course.Publish("someone-else", Now);

        result.Errors.HasCode("course.not_owner").Should().BeTrue();
        course.Status.Should().Be(CourseStatus.Draft);
    }

    [Fact]
    public void A_draft_course_refuses_reservations()
    {
        var course = Course.Create(Guid.NewGuid(), "dotnet-101", "Intro", 10, Teacher, Now).Value;

        var result = course.ReserveSeat("student-1", "key-1", Now, Hold);

        result.Errors.HasCode("course.not_open").Should().BeTrue();
    }

    [Fact]
    public void Reserving_twice_with_the_same_key_returns_the_same_seat()
    {
        var course = PublishedCourse();

        var first = course.ReserveSeat("student-1", "key-1", Now, Hold);
        var retried = course.ReserveSeat("student-1", "key-1", Now.AddSeconds(5), Hold);

        retried.Value.Id.Should().Be(first.Value.Id);
        course.SeatsTaken(Now).Should().Be(1);
    }

    [Fact]
    public void A_student_cannot_hold_two_seats_in_the_same_course()
    {
        var course = PublishedCourse();
        course.ReserveSeat("student-1", "key-1", Now, Hold);

        var result = course.ReserveSeat("student-1", "key-2", Now, Hold);

        result.Errors.HasCode("course.already_enrolled").Should().BeTrue();
    }

    [Fact]
    public void The_course_is_full_when_every_seat_is_taken()
    {
        var course = PublishedCourse(capacity: 1);
        course.ReserveSeat("student-1", "key-1", Now, Hold);

        var result = course.ReserveSeat("student-2", "key-2", Now, Hold);

        result.Errors.HasCode("course.full").Should().BeTrue();
    }

    [Fact]
    public void An_expired_hold_gives_its_seat_back()
    {
        var course = PublishedCourse(capacity: 1);
        course.ReserveSeat("student-1", "key-1", Now, Hold);

        var later = Now + Hold + TimeSpan.FromSeconds(1);
        var result = course.ReserveSeat("student-2", "key-2", later, Hold);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Confirming_is_idempotent_but_an_expired_hold_cannot_be_confirmed()
    {
        var course = PublishedCourse();
        var reservation = course.ReserveSeat("student-1", "key-1", Now, Hold).Value;
        var expired = course.ReserveSeat("student-2", "key-2", Now, Hold).Value;

        course.ConfirmSeat(reservation.Id, Now.AddMinutes(1)).IsSuccess.Should().BeTrue();
        course.ConfirmSeat(reservation.Id, Now.AddMinutes(2)).IsSuccess.Should().BeTrue();
        course.ConfirmSeat(expired.Id, Now + Hold).Errors.HasCode("reservation.expired").Should().BeTrue();

        // A confirmed seat never expires.
        course.SeatsTaken(Now.AddDays(30)).Should().Be(1);
    }

    [Fact]
    public void Releasing_is_idempotent_and_frees_the_seat()
    {
        var course = PublishedCourse(capacity: 1);
        var reservation = course.ReserveSeat("student-1", "key-1", Now, Hold).Value;

        course.ReleaseSeat(reservation.Id).IsSuccess.Should().BeTrue();
        course.ReleaseSeat(reservation.Id).IsSuccess.Should().BeTrue();
        course.ReleaseSeat(Guid.NewGuid()).IsSuccess.Should().BeTrue();

        course.SeatsLeft(Now).Should().Be(1);
        course.ReserveSeat("student-1", "key-1", Now, Hold).Errors.HasCode("reservation.released").Should().BeTrue();
    }
}
