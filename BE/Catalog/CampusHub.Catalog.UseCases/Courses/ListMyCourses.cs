using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using FluentResults;

namespace Dev4u.CampusHub.Catalog.UseCases.Courses;

/// <summary>The courses taught by the caller, drafts included (the teacher publishes them from there).</summary>
[Export, Scoped]
public sealed class ListMyCourses(ICourseRepository courses, TimeProvider timeProvider)
{
    public const int MaxCount = 100;

    public async Task<Result<IReadOnlyList<CourseDto>>> ExecuteAsync(string teacherId, CancellationToken cancellationToken)
    {
        var mine = await courses.ListByTeacherAsync(teacherId, MaxCount, cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        return Result.Ok<IReadOnlyList<CourseDto>>([.. mine.Select(c => c.ToDto(now))]);
    }
}
