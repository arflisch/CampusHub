using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using FluentResults;

namespace Dev4u.CampusHub.Catalog.UseCases.Courses;

/// <summary>Reads one course. Drafts are only visible to their teacher.</summary>
[Export, Scoped]
public sealed class GetCourse(ICourseRepository courses, TimeProvider timeProvider)
{
    public async Task<Result<CourseDto>> ExecuteAsync(Guid courseId, string userId, CancellationToken cancellationToken)
    {
        var course = await courses.GetAsync(courseId, cancellationToken).ConfigureAwait(false);

        // A draft of someone else answers "not found": its existence is not public information.
        if (course is null || (course.Status == CourseStatus.Draft && !course.IsTaughtBy(userId)))
        {
            return Result.Fail<CourseDto>(CourseErrors.NotFound(courseId));
        }

        return Result.Ok(course.ToDto(timeProvider.GetUtcNow()));
    }
}
