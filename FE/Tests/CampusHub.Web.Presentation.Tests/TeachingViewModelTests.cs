using AwesomeAssertions;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.ViewModels;
using FluentResults;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Dev4u.CampusHub.Web.Presentation.Tests;

public class TeachingViewModelTests : IAsyncLifetime
{
    private static readonly CurrentUser Teacher = new("teacher-1", "Ada Teacher", IsTeacher: true);

    private readonly FakeCourseCatalog _catalog = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 22, 15, 0, 0, TimeSpan.Zero));
    private readonly TeachingViewModel _sut;

    private readonly FakeEnrollmentService _enrollments = new();

    public TeachingViewModelTests() => _sut = new TeachingViewModel(_catalog, new FakeCurrentUserService(Teacher), _time, _enrollments);

    public Task InitializeAsync() => _sut.InitializeAsync(CancellationToken.None);

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_user_without_the_teacher_role_gets_nothing_to_create_and_learns_their_id()
    {
        var student = new TeachingViewModel(_catalog, new FakeCurrentUserService(new CurrentUser("student-1", "Sam Student")), _time, _enrollments);
        _catalog.Mine = Result.Fail<IReadOnlyList<CourseItem>>("must not be called");

        await student.InitializeAsync(CancellationToken.None);
        student.Code = "DOTNET-101";
        student.Title = "Modern .NET";

        student.IsTeacher.Should().BeFalse();
        student.UserId.Should().Be("student-1");
        student.ErrorMessage.Should().BeNull("the teacher's courses are not even loaded");
        student.CreateCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void An_empty_form_shows_no_error_but_cannot_be_submitted()
    {
        _sut.CodeError.Should().BeNull();
        _sut.CreateCommand.CanExecute(null).Should().BeFalse();
    }

    [Theory]
    [InlineData("AB", "Between 3 and 20 characters.")]
    [InlineData("DOTNET 101", "Letters, digits and dashes only.")]
    [InlineData("DOTNET-101", null)]
    public void The_code_is_validated_as_the_user_types(string code, string? expectedError)
    {
        _sut.Code = code;
        _sut.CodeError.Should().Be(expectedError);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(TeachingViewModel.MaxCapacity + 1)]
    public void An_out_of_range_capacity_blocks_the_creation(int capacity)
    {
        _sut.Code = "DOTNET-101";
        _sut.Title = "Modern .NET";
        _sut.Capacity = capacity;

        _sut.CapacityError.Should().NotBeNull();
        _sut.CreateCommand.CanExecute(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 30, "The course must start after today.")]
    [InlineData(7, -1, "The last day comes after the first one.")]
    [InlineData(7, 366, "A course lasts at most a year.")]
    [InlineData(1, 0, null)]
    public void The_schedule_is_validated_as_the_user_types(int startsInDays, int lastsDays, string? expectedError)
    {
        var today = DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);
        _sut.Code = "DOTNET-101";
        _sut.Title = "Modern .NET";

        _sut.StartsOn = today.AddDays(startsInDays);
        _sut.EndsOn = _sut.StartsOn.AddDays(lastsDays);

        _sut.ScheduleError.Should().Be(expectedError);
        _sut.CreateCommand.CanExecute(null).Should().Be(expectedError is null);
    }

    [Theory]
    [InlineData("-1", "Between 0 (free) and 10000 €.")]
    [InlineData("10000.01", "Between 0 (free) and 10000 €.")]
    [InlineData("49.999", "Two decimals at most.")]
    [InlineData("0", null)]
    [InlineData("49.90", null)]
    public void The_price_is_validated_as_the_user_types(string price, string? expectedError)
    {
        _sut.Price = decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture);

        _sut.PriceError.Should().Be(expectedError);
    }

    [Fact]
    public async Task A_created_course_is_listed_first_and_the_form_is_reset()
    {
        _sut.Code = " dotnet-101 ";
        _sut.Title = " Modern .NET ";
        _sut.Capacity = 25;
        _sut.Price = 49.90m;
        _sut.CreateCommand.CanExecute(null).Should().BeTrue();

        await _sut.CreateCommand.ExecuteAsync(null);

        _catalog.Created.Should().Be(("dotnet-101", "Modern .NET", 25, 4990,
            new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 28, 17, 0, 0, TimeSpan.Zero)),
            "the default schedule starts next week, for a month, from 9:00 on the first day to 17:00 on the last (UTC)");
        _sut.MyCourses[0].Title.Should().Be("Modern .NET");
        _sut.Code.Should().BeEmpty();
        _sut.CreateCommand.CanExecute(null).Should().BeFalse();
        _sut.InfoMessage.Should().Contain("draft");
    }

    [Fact]
    public async Task A_refused_creation_keeps_the_form_and_explains_why()
    {
        _catalog.CreateResult = TestData.Refused<CourseItem>("course.code_taken");
        _sut.Code = "DOTNET-101";
        _sut.Title = "Modern .NET";

        await _sut.CreateCommand.ExecuteAsync(null);

        _sut.ErrorMessage.Should().Be("A course with this code already exists.");
        _sut.Code.Should().Be("DOTNET-101");
    }

    [Fact]
    public async Task Publishing_replaces_the_draft_in_the_list()
    {
        var draft = TestData.Course(state: CourseState.Draft);
        _catalog.Mine = Result.Ok<IReadOnlyList<CourseItem>>([draft]);
        await _sut.LoadCommand.ExecuteAsync(null);

        await _sut.PublishCommand.ExecuteAsync(draft);

        _sut.MyCourses.Single().State.Should().Be(CourseState.Published);
    }

    [Fact]
    public async Task A_published_course_that_has_not_started_can_be_cancelled()
    {
        var published = TestData.Course(); // starts 2026-11-02, "now" is 2026-10-22
        _catalog.Mine = Result.Ok<IReadOnlyList<CourseItem>>([published]);
        await _sut.LoadCommand.ExecuteAsync(null);

        _sut.CancelCourseCommand.CanExecute(published).Should().BeTrue();
        await _sut.CancelCourseCommand.ExecuteAsync(published);

        _sut.MyCourses.Single().State.Should().Be(CourseState.Cancelled);
        _sut.InfoMessage.Should().Contain("refunded");
        _sut.CancelCourseCommand.CanExecute(_sut.MyCourses.Single()).Should().BeFalse("already cancelled");
    }

    [Fact]
    public async Task The_participants_of_a_course_are_shown_then_hidden()
    {
        var course = TestData.Course();
        _enrollments.Participants = Result.Ok<IReadOnlyList<ParticipantItem>>(
            [new ParticipantItem(Guid.NewGuid(), "student-1", "Ada Lovelace", EnrollmentState.Confirmed, 0, DateTimeOffset.UnixEpoch)]);

        await _sut.ToggleParticipantsCommand.ExecuteAsync(course);
        _sut.ShownParticipantsOf.Should().Be(course.Id);
        _sut.Participants.Single().DisplayName.Should().Be("Ada Lovelace");

        await _sut.ToggleParticipantsCommand.ExecuteAsync(course);
        _sut.ShownParticipantsOf.Should().BeNull();
    }
}
