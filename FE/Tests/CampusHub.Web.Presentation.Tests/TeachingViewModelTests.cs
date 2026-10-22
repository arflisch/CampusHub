using AwesomeAssertions;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.ViewModels;
using FluentResults;
using Xunit;

namespace Dev4u.CampusHub.Web.Presentation.Tests;

public class TeachingViewModelTests
{
    private readonly FakeCourseCatalog _catalog = new();
    private readonly TeachingViewModel _sut;

    public TeachingViewModelTests() => _sut = new TeachingViewModel(_catalog);

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
    public void The_code_is_validated_as_the_user_types(string code, string? expectedError) =>
        new TeachingViewModel(_catalog) { Code = code }.CodeError.Should().Be(expectedError);

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

    [Fact]
    public async Task A_created_course_is_listed_first_and_the_form_is_reset()
    {
        _sut.Code = " dotnet-101 ";
        _sut.Title = " Modern .NET ";
        _sut.Capacity = 25;
        _sut.CreateCommand.CanExecute(null).Should().BeTrue();

        await _sut.CreateCommand.ExecuteAsync(null);

        _catalog.Created.Should().Be(("dotnet-101", "Modern .NET", 25));
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
        await _sut.InitializeAsync(CancellationToken.None);

        await _sut.PublishCommand.ExecuteAsync(draft);

        _sut.MyCourses.Single().State.Should().Be(CourseState.Published);
    }
}
