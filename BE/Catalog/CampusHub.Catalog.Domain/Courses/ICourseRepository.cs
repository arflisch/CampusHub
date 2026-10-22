using FluentResults;

namespace Dev4u.CampusHub.Catalog.Domain.Courses;

/// <summary>
/// Port implemented by the MongoDB adapter. Saving uses optimistic concurrency on <see cref="Course.Version"/>:
/// a concurrent update makes <see cref="SaveAsync"/> fail with <c>course.concurrency_conflict</c>.
/// </summary>
public interface ICourseRepository
{
    Task<Course?> GetAsync(Guid courseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Course>> ListPublishedAsync(int skip, int take, CancellationToken cancellationToken);

    Task<IReadOnlyList<Course>> ListByTeacherAsync(string teacherId, int take, CancellationToken cancellationToken);

    Task<Result> AddAsync(Course course, CancellationToken cancellationToken);

    Task<Result> SaveAsync(Course course, CancellationToken cancellationToken);
}
