using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using Dev4u.CampusHub.Catalog.UseCases.Common;
using FluentResults;

namespace Dev4u.CampusHub.Catalog.UseCases.Courses;

/// <summary>
/// The teacher who owns a course cancels it before it starts. The course only records the cancellation:
/// <see cref="AnnounceCourseCancellations"/> then tells Enrollment, which refunds and notifies every student.
/// </summary>
[Export, Scoped]
public sealed class CancelCourse(ICourseRepository courses, TimeProvider timeProvider)
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
            var wasCancelled = course.Status == CourseStatus.Cancelled;
            var cancelled = course.Cancel(userId, now);
            if (cancelled.IsFailed)
            {
                return cancelled.ToResult<CourseDto>();
            }

            var saved = await courses.SaveAsync(course, ct).ConfigureAwait(false);
            if (saved.IsSuccess && !wasCancelled)
            {
                CatalogMetrics.RecordCancelled();
            }

            return saved.IsFailed ? saved.ToResult<CourseDto>() : Result.Ok(course.ToDto(now));
        }, cancellationToken).ConfigureAwait(false);
}

/// <summary>
/// Publishes the cancellations not announced yet, then records it on each course. Run in a loop by the host: a
/// publication that fails is simply retried at the next run (same event id: subscribers deduplicate). Returns the
/// number of courses announced.
/// </summary>
[Export, Scoped]
public sealed class AnnounceCourseCancellations(ICourseRepository courses, ICourseCancellationAnnouncer announcer, TimeProvider timeProvider)
{
    public const int BatchSize = 20;

    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var announced = 0;
        var pending = await courses.ListCancellationsToAnnounceAsync(BatchSize, cancellationToken).ConfigureAwait(false);
        foreach (var course in pending)
        {
            if ((await announcer.AnnounceAsync(course, cancellationToken).ConfigureAwait(false)).IsFailed)
            {
                continue;
            }

            course.MarkCancellationAnnounced(timeProvider.GetUtcNow());
            // A concurrency conflict means another instance recorded it: announced either way.
            if ((await courses.SaveAsync(course, cancellationToken).ConfigureAwait(false)).IsSuccess)
            {
                announced++;
            }
        }

        return announced;
    }
}
