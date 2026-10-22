using Arc4u.MongoDB;
using MongoDB.Driver;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

/// <summary>Indexes of the Enrollment database, created idempotently at startup by the host.</summary>
public static class EnrollmentIndexes
{
    public static async Task EnsureAsync(IMongoClientFactory<DatabaseContext> clientFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);

        var outbox = clientFactory.GetCollection<OutboxMessage>();
        await outbox.Indexes.CreateManyAsync(
        [
            // What the dispatcher queries: pending messages, oldest first.
            new CreateIndexModel<OutboxMessage>(
                Builders<OutboxMessage>.IndexKeys.Ascending(m => m.ProcessedAtUtc).Ascending(m => m.FailedAtUtc).Ascending(m => m.OccurredAtUtc),
                new CreateIndexOptions { Name = "pending" }),
            // Processed messages are purged after a week; a TTL index ignores pending ones (null date).
            new CreateIndexModel<OutboxMessage>(
                Builders<OutboxMessage>.IndexKeys.Ascending(m => m.ProcessedAtUtc),
                new CreateIndexOptions { Name = "processed_ttl", ExpireAfter = TimeSpan.FromDays(7) }),
        ], cancellationToken).ConfigureAwait(false);

        var enrollments = clientFactory.GetCollection<EnrollmentDocument>();
        await enrollments.Indexes.CreateOneAsync(
            new CreateIndexModel<EnrollmentDocument>(
                Builders<EnrollmentDocument>.IndexKeys.Ascending(e => e.StudentId).Descending(e => e.RequestedAtUtc),
                new CreateIndexOptions { Name = "student_requests" }),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
