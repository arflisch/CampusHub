using Arc4u.Dependency.Attribute;
using Arc4u.MongoDB;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using FluentResults;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

/// <summary>
/// MongoDB implementation of <see cref="IEnrollmentRepository"/>: the enrollment and its outbox messages are
/// written in a single multi-document transaction (transactional outbox).
/// </summary>
[Export(typeof(IEnrollmentRepository)), Scoped]
public sealed class MongoEnrollmentRepository(IMongoClientFactory<DatabaseContext> clientFactory, ILogger<MongoEnrollmentRepository> logger)
    : IEnrollmentRepository
{
    private IMongoCollection<EnrollmentDocument> Enrollments => clientFactory.GetCollection<EnrollmentDocument>();

    private IMongoCollection<OutboxMessage> Outbox => clientFactory.GetCollection<OutboxMessage>();

    public async Task<StudentEnrollment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var document = await Enrollments.Find(e => e.Id == enrollmentId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return document?.ToDomain();
    }

    public async Task<IReadOnlyList<StudentEnrollment>> ListForStudentAsync(string studentId, int take, CancellationToken cancellationToken)
    {
        var documents = await Enrollments.Find(e => e.StudentId == studentId)
                                         .SortByDescending(e => e.RequestedAtUtc)
                                         .Limit(take)
                                         .ToListAsync(cancellationToken)
                                         .ConfigureAwait(false);
        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyList<StudentEnrollment>> ListStalledAsync(DateTimeOffset updatedBefore, int take, CancellationToken cancellationToken)
    {
        var limit = updatedBefore.UtcDateTime;
        var documents = await Enrollments.Find(e => (e.Status == EnrollmentStatus.Requested || e.Status == EnrollmentStatus.SeatHeld)
                                                     && e.UpdatedAtUtc < limit)
                                         .SortBy(e => e.UpdatedAtUtc)
                                         .Limit(take)
                                         .ToListAsync(cancellationToken)
                                         .ConfigureAwait(false);
        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<IReadOnlyList<StudentEnrollment>> ListDueForReminderAsync(DateTimeOffset from, DateTimeOffset to, int take,
                                                                                CancellationToken cancellationToken)
    {
        var (start, end) = (from.UtcDateTime, to.UtcDateTime);
        var documents = await Enrollments.Find(e => e.Status == EnrollmentStatus.Confirmed && e.ReminderSentAtUtc == null
                                                     && e.CourseStartsAtUtc >= start && e.CourseStartsAtUtc < end)
                                         .SortBy(e => e.CourseStartsAtUtc)
                                         .Limit(take)
                                         .ToListAsync(cancellationToken)
                                         .ConfigureAwait(false);
        return [.. documents.Select(d => d.ToDomain())];
    }

    public async Task<Result> AddAsync(StudentEnrollment enrollment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        try
        {
            return await SaveWithOutboxAsync(enrollment, async (session, ct) =>
            {
                await Enrollments.InsertOneAsync(session, enrollment.ToDocument(enrollment.Version + 1), cancellationToken: ct).ConfigureAwait(false);
                return true;
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Two identical requests raced: the other one created it. The caller reloads and continues.
            return Result.Fail(EnrollmentErrors.ConcurrencyConflict(enrollment.Id));
        }
    }

    public Task<Result> SaveAsync(StudentEnrollment enrollment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(enrollment);
        var expectedVersion = enrollment.Version;

        return SaveWithOutboxAsync(enrollment, async (session, ct) =>
        {
            var replaced = await Enrollments.ReplaceOneAsync(session,
                e => e.Id == enrollment.Id && e.Version == expectedVersion,
                enrollment.ToDocument(expectedVersion + 1),
                cancellationToken: ct).ConfigureAwait(false);

            if (replaced.MatchedCount == 0)
            {
                // Someone saved a newer version: give up this transaction, no outbox message is written.
                await session.AbortTransactionAsync(ct).ConfigureAwait(false);
                return false;
            }

            return true;
        }, cancellationToken);
    }

    private async Task<Result> SaveWithOutboxAsync(StudentEnrollment enrollment,
                                                   Func<IClientSessionHandle, CancellationToken, Task<bool>> write,
                                                   CancellationToken cancellationToken)
    {
        // Built before the transaction: WithTransactionAsync may run the callback several times.
        var messages = OutboxMessages.From(enrollment.DomainEvents);

        using var session = await clientFactory.CreateClient().StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var written = await session.WithTransactionAsync(async (s, ct) =>
        {
            // Every operation receives the session, otherwise it would silently run outside the transaction.
            if (!await write(s, ct).ConfigureAwait(false))
            {
                return false; // the callback aborted the transaction: WithTransactionAsync does not commit
            }

            if (messages.Count > 0)
            {
                await Outbox.InsertManyAsync(s, messages, cancellationToken: ct).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!written)
        {
            logger.LogNoDocumentMatched(DatabaseContext.EnrollmentsCollection);
            return Result.Fail(EnrollmentErrors.ConcurrencyConflict(enrollment.Id));
        }

        enrollment.MarkPersisted();
        enrollment.ClearDomainEvents();
        logger.LogDocumentsPersisted(1 + messages.Count);
        return Result.Ok();
    }
}
