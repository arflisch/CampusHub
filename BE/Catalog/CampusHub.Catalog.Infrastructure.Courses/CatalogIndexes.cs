using Arc4u.MongoDB;
using MongoDB.Driver;

namespace Dev4u.CampusHub.Catalog.Infrastructure.Courses;

/// <summary>Indexes of the Catalog database, created idempotently at startup by the Host.</summary>
public static class CatalogIndexes
{
    public static async Task EnsureAsync(IMongoClientFactory<DatabaseContext> clientFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);

        var courses = clientFactory.GetCollection<CourseDocument>();
        await courses.Indexes.CreateManyAsync(
        [
            // Course codes are unique: enforced by the database, not by a racy "exists?" query.
            new CreateIndexModel<CourseDocument>(
                Builders<CourseDocument>.IndexKeys.Ascending(c => c.Code),
                new CreateIndexOptions { Name = "code_unique", Unique = true }),
            // Catalogue page: published courses, newest first.
            new CreateIndexModel<CourseDocument>(
                Builders<CourseDocument>.IndexKeys.Ascending(c => c.Status).Descending(c => c.PublishedAtUtc),
                new CreateIndexOptions { Name = "status_published" }),
            // "My courses" page of a teacher.
            new CreateIndexModel<CourseDocument>(
                Builders<CourseDocument>.IndexKeys.Ascending(c => c.TeacherId).Descending(c => c.CreatedAtUtc),
                new CreateIndexOptions { Name = "teacher_courses" }),
        ], cancellationToken).ConfigureAwait(false);
    }
}
