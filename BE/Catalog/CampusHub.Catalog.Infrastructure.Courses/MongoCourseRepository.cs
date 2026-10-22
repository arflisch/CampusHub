using Arc4u.Dependency.Attribute;
using Arc4u.MongoDB;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using FluentResults;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Dev4u.CampusHub.Catalog.Infrastructure.Courses;

/// <summary>
/// MongoDB implementation of <see cref="ICourseRepository"/>. Saves are conditional on the version that was
/// read, which turns a lost race into a <c>course.concurrency_conflict</c> failure instead of a silent overwrite.
/// </summary>
[Export(typeof(ICourseRepository)), Scoped]
public sealed class MongoCourseRepository(IMongoClientFactory<DatabaseContext> clientFactory, ILogger<MongoCourseRepository> logger)
    : ICourseRepository
{
    private IMongoCollection<CourseDocument> Courses => clientFactory.GetCollection<CourseDocument>();

    public async Task<Course?> GetAsync(Guid courseId, CancellationToken cancellationToken)
    {
        var document = await Courses.Find(c => c.Id == courseId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return document?.ToDomain();
    }

    public async Task<IReadOnlyList<Course>> ListPublishedAsync(int skip, int take, CancellationToken cancellationToken)
    {
        var documents = await Courses.Find(c => c.Status == CourseStatus.Published)
                                     .SortByDescending(c => c.PublishedAtUtc)
                                     .Skip(skip)
                                     .Limit(take)
                                     .ToListAsync(cancellationToken)
                                     .ConfigureAwait(false);
        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyList<Course>> ListByTeacherAsync(string teacherId, int take, CancellationToken cancellationToken)
    {
        var documents = await Courses.Find(c => c.TeacherId == teacherId)
                                     .SortByDescending(c => c.CreatedAtUtc)
                                     .Limit(take)
                                     .ToListAsync(cancellationToken)
                                     .ConfigureAwait(false);
        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<Result> AddAsync(Course course, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(course);
        try
        {
            await Courses.InsertOneAsync(course.ToDocument(course.Version + 1), cancellationToken: cancellationToken).ConfigureAwait(false);
            course.MarkPersisted();
            logger.LogDocumentsPersisted(1);
            return Result.Ok();
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return Result.Fail(CourseErrors.CodeTaken(course.Code));
        }
    }

    public async Task<Result> SaveAsync(Course course, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(course);

        var expectedVersion = course.Version;
        var replaced = await Courses.ReplaceOneAsync(
                c => c.Id == course.Id && c.Version == expectedVersion,
                course.ToDocument(expectedVersion + 1),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (replaced.MatchedCount == 0)
        {
            logger.LogNoDocumentMatched(DatabaseContext.CoursesCollection);
            return Result.Fail(CourseErrors.ConcurrencyConflict(course.Id));
        }

        course.MarkPersisted();
        logger.LogDocumentsPersisted(1);
        return Result.Ok();
    }
}
