using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using Dev4u.CampusHub.Catalog.UseCases.Common;
using FluentResults;

namespace Dev4u.CampusHub.Catalog.UseCases.Courses;

/// <summary>The teacher who owns a course opens it for enrollment.</summary>
[Export, Scoped]
public sealed class PublishCourse(ICourseRepository courses, TimeProvider timeProvider)
{
    public async Task<Result<CourseDto>> ExecuteAsync(Guid courseId, string userId, CancellationToken cancellationToken) =>
        await ConcurrencyRetry.ExecuteAsync(async ct =>
        {
            var course = await courses.GetAsync(courseId, ct).ConfigureAwait(false);
            if (course is null)
            {
                return Result.Fail<CourseDto>(CourseErrors.NotFound(courseId));
            }

            var now = timeProvider.GetUtcNow();
            var published = course.Publish(userId, now);
            if (published.IsFailed)
            {
                return published.ToResult<CourseDto>();
            }

            var saved = await courses.SaveAsync(course, ct).ConfigureAwait(false);
            return saved.IsFailed ? saved.ToResult<CourseDto>() : Result.Ok(course.ToDto(now));
        }, cancellationToken).ConfigureAwait(false);
}
