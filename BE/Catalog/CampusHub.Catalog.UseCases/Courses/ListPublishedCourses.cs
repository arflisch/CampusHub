using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using FluentResults;

namespace Dev4u.CampusHub.Catalog.UseCases.Courses;

/// <summary>Catalogue page of the courses open for enrollment.</summary>
[Export, Scoped]
public sealed class ListPublishedCourses(ICourseRepository courses, TimeProvider timeProvider)
{
    public const int MaxPageSize = 100;

    public async Task<Result<IReadOnlyList<CourseDto>>> ExecuteAsync(int skip, int take, CancellationToken cancellationToken)
    {
        // Page size is capped so a client can't ask for the whole collection in one call.
        var page = await courses.ListPublishedAsync(Math.Max(0, skip), Math.Clamp(take, 1, MaxPageSize), cancellationToken)
                                .ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        return Result.Ok<IReadOnlyList<CourseDto>>([.. page.Select(c => c.ToDto(now))]);
    }
}
