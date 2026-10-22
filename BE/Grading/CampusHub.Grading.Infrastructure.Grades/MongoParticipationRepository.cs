using Arc4u.Dependency.Attribute;
using Arc4u.MongoDB;
using Dev4u.CampusHub.Grading.Domain.Certificates;
using FluentResults;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Dev4u.CampusHub.Grading.Infrastructure.Grades;

/// <summary>Storage shape of a <see cref="CourseParticipation"/>; the domain stays free of persistence attributes.</summary>
[BsonIgnoreExtraElements]
public sealed class ParticipationDocument
{
    /// <summary>The enrollment id: the primary key doubles as the inbox of EnrollmentConfirmed.</summary>
    [BsonId]
    public Guid Id { get; set; }

    public Guid CourseId { get; set; }

    public string CourseTitle { get; set; } = string.Empty;

    public string StudentId { get; set; } = string.Empty;

    public DateTime CourseEndsAtUtc { get; set; }

    public string? CertificateNumber { get; set; }

    public DateTime? CertificateIssuedAtUtc { get; set; }

    public DateTime? CertificateAnnouncedAtUtc { get; set; }

    public long Version { get; set; }
}

/// <summary>MongoDB implementation of <see cref="IParticipationRepository"/>, with optimistic concurrency on Version.</summary>
[Export(typeof(IParticipationRepository)), Scoped]
public sealed class MongoParticipationRepository(IMongoClientFactory<DatabaseContext> clientFactory) : IParticipationRepository
{
    private IMongoCollection<ParticipationDocument> Participations => clientFactory.GetCollection<ParticipationDocument>();

    public async Task<bool> AddIfAbsentAsync(CourseParticipation participation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participation);
        try
        {
            await Participations.InsertOneAsync(ToDocument(participation, participation.Version + 1), cancellationToken: cancellationToken)
                                .ConfigureAwait(false);
            participation.MarkPersisted();
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false; // the event was already processed
        }
    }

    public async Task<CourseParticipation?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var document = await Participations.Find(p => p.Id == enrollmentId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<Result> SaveAsync(CourseParticipation participation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participation);
        var expectedVersion = participation.Version;
        var replaced = await Participations.ReplaceOneAsync(p => p.Id == participation.EnrollmentId && p.Version == expectedVersion,
                                                            ToDocument(participation, expectedVersion + 1),
                                                            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (replaced.MatchedCount == 0)
        {
            return Result.Fail(GradingErrors.ConcurrencyConflict(participation.EnrollmentId));
        }

        participation.MarkPersisted();
        return Result.Ok();
    }

    public async Task<IReadOnlyList<CourseParticipation>> ListEndedWithoutCertificateAsync(DateTimeOffset now, int take,
                                                                                         CancellationToken cancellationToken)
    {
        var limit = now.UtcDateTime;
        var documents = await Participations.Find(p => p.CertificateNumber == null && p.CourseEndsAtUtc <= limit)
                                            .SortBy(p => p.CourseEndsAtUtc)
                                            .Limit(take)
                                            .ToListAsync(cancellationToken)
                                            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    public async Task<IReadOnlyList<CourseParticipation>> ListUnannouncedAsync(int take, CancellationToken cancellationToken)
    {
        var documents = await Participations.Find(p => p.CertificateNumber != null && p.CertificateAnnouncedAtUtc == null)
                                            .SortBy(p => p.CertificateIssuedAtUtc)
                                            .Limit(take)
                                            .ToListAsync(cancellationToken)
                                            .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    private static ParticipationDocument ToDocument(CourseParticipation participation, long version) => new()
    {
        Id = participation.EnrollmentId,
        CourseId = participation.CourseId,
        CourseTitle = participation.CourseTitle,
        StudentId = participation.StudentId,
        CourseEndsAtUtc = participation.CourseEndsAt.UtcDateTime,
        CertificateNumber = participation.Certificate?.Number,
        CertificateIssuedAtUtc = participation.Certificate?.IssuedAt.UtcDateTime,
        CertificateAnnouncedAtUtc = participation.CertificateAnnouncedAt?.UtcDateTime,
        Version = version,
    };

    private static CourseParticipation ToDomain(ParticipationDocument document) =>
        CourseParticipation.Rehydrate(
            document.Id, document.CourseId, document.CourseTitle, document.StudentId, AsUtc(document.CourseEndsAtUtc),
            document is { CertificateNumber: { } number, CertificateIssuedAtUtc: { } issuedAt } ? new Certificate(number, AsUtc(issuedAt)) : null,
            document.CertificateAnnouncedAtUtc is { } announcedAt ? AsUtc(announcedAt) : null,
            document.Version);

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>Indexes of the Grading database, created idempotently at startup by the host.</summary>
public static class GradingIndexes
{
    public static Task EnsureAsync(IMongoClientFactory<DatabaseContext> clientFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        return clientFactory.GetCollection<ParticipationDocument>().Indexes.CreateManyAsync(
        [
            // The certificates job: participations without certificate, by end of course.
            new CreateIndexModel<ParticipationDocument>(
                Builders<ParticipationDocument>.IndexKeys.Ascending(p => p.CertificateNumber).Ascending(p => p.CourseEndsAtUtc),
                new CreateIndexOptions { Name = "certificates_due" }),
            // Certificates issued but not announced yet.
            new CreateIndexModel<ParticipationDocument>(
                Builders<ParticipationDocument>.IndexKeys.Ascending(p => p.CertificateAnnouncedAtUtc).Ascending(p => p.CertificateIssuedAtUtc),
                new CreateIndexOptions { Name = "certificates_unannounced" }),
        ], cancellationToken);
    }
}
