using AwesomeAssertions;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.ViewModels;
using FluentResults;
using Xunit;

namespace Dev4u.CampusHub.Web.Presentation.Tests;

public class CatalogViewModelTests
{
    private readonly FakeCourseCatalog _catalog = new();
    private readonly FakeEnrollmentService _enrollments = new();
    private readonly CatalogViewModel _sut;

    public CatalogViewModelTests() => _sut = new CatalogViewModel(_catalog, _enrollments);

    [Fact]
    public async Task A_retry_after_a_transient_failure_reuses_the_same_request_id()
    {
        var course = TestData.Course();
        _enrollments.AnswerRequest(TestData.Transient<EnrollmentItem>());

        await _sut.EnrollCommand.ExecuteAsync(course);
        await _sut.EnrollCommand.ExecuteAsync(course);

        _enrollments.RequestIds.Should().HaveCount(2);
        _enrollments.RequestIds.Distinct().Should().ContainSingle("the server must see the second click as the same request");
        _sut.WasRequested(course).Should().BeTrue();
    }

    [Fact]
    public async Task After_a_definitive_refusal_a_new_click_is_a_new_request()
    {
        var course = TestData.Course();
        _enrollments.AnswerRequest(TestData.Refused<EnrollmentItem>("course.not_open"));

        await _sut.EnrollCommand.ExecuteAsync(course);
        _sut.ErrorMessage.Should().Be("This course is not open for enrollment.");

        await _sut.EnrollCommand.ExecuteAsync(course);
        _enrollments.RequestIds.Distinct().Should().HaveCount(2);
    }

    [Fact]
    public async Task A_course_can_be_requested_only_once_per_visit()
    {
        var course = TestData.Course();
        _sut.EnrollCommand.CanExecute(course).Should().BeTrue();

        await _sut.EnrollCommand.ExecuteAsync(course);

        _sut.EnrollCommand.CanExecute(course).Should().BeFalse();
        _sut.InfoMessage.Should().Contain(course.Title);
    }

    [Fact]
    public void Full_or_unpublished_courses_cannot_be_requested()
    {
        _sut.EnrollCommand.CanExecute(TestData.Course(seatsLeft: 0)).Should().BeFalse();
        _sut.EnrollCommand.CanExecute(TestData.Course(state: CourseState.Draft)).Should().BeFalse();
    }

    [Fact]
    public async Task A_failed_load_shows_a_message_the_user_can_act_on()
    {
        _catalog.Published = TestData.Transient<IReadOnlyList<CourseItem>>();

        await _sut.InitializeAsync(CancellationToken.None);

        _sut.ErrorMessage.Should().Be("The server can't be reached. Check your connection and try again.");
        _sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Loading_shows_the_published_courses()
    {
        _catalog.Published = Result.Ok<IReadOnlyList<CourseItem>>([TestData.Course(), TestData.Course("Dapr")]);

        await _sut.InitializeAsync(CancellationToken.None);

        _sut.Courses.Should().HaveCount(2);
        _sut.ErrorMessage.Should().BeNull();
    }
}
