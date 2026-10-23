using Arc4u.Dependency.Attribute;
using Arc4u.MongoDB;
using Dev4u.CampusHub.Billing.Domain.Payments;
using FluentResults;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Dev4u.CampusHub.Billing.Infrastructure.Payments;

/// <summary>Storage shape of a <see cref="Payment"/>; the domain stays free of persistence attributes.</summary>
[BsonIgnoreExtraElements]
public sealed class PaymentDocument
{
    /// <summary>The enrollment id: one payment per enrollment, enforced by the primary key.</summary>
    [BsonId]
    public Guid Id { get; set; }

    public string StudentId { get; set; } = string.Empty;

    public Guid CourseId { get; set; }

    public int AmountInCents { get; set; }

    [BsonRepresentation(BsonType.String)]
    public PaymentStatus Status { get; set; }

    public string? ProviderReference { get; set; }

    public string? DeclineReason { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? RefundedAtUtc { get; set; }

    public long Version { get; set; }
}

/// <summary>MongoDB implementation of <see cref="IPaymentRepository"/>, with optimistic concurrency on Version.</summary>
[Export(typeof(IPaymentRepository)), Scoped]
public sealed class MongoPaymentRepository(IMongoClientFactory<DatabaseContext> clientFactory) : IPaymentRepository
{
    private IMongoCollection<PaymentDocument> Payments => clientFactory.GetCollection<PaymentDocument>();

    public async Task<Payment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var document = await Payments.Find(p => p.Id == enrollmentId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return document is null ? null : ToDomain(document);
    }

    public async Task<bool> AddIfAbsentAsync(Payment payment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        try
        {
            await Payments.InsertOneAsync(ToDocument(payment, payment.Version + 1), cancellationToken: cancellationToken).ConfigureAwait(false);
            payment.MarkPersisted();
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public async Task<Result> SaveAsync(Payment payment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        var expectedVersion = payment.Version;
        var replaced = await Payments.ReplaceOneAsync(p => p.Id == payment.EnrollmentId && p.Version == expectedVersion,
                                                      ToDocument(payment, expectedVersion + 1),
                                                      cancellationToken: cancellationToken).ConfigureAwait(false);
        if (replaced.MatchedCount == 0)
        {
            return Result.Fail(PaymentErrors.ConcurrencyConflict(payment.EnrollmentId));
        }

        payment.MarkPersisted();
        return Result.Ok();
    }

    public async Task<IReadOnlyList<Payment>> ListForStudentAsync(string studentId, int take, CancellationToken cancellationToken)
    {
        var documents = await Payments.Find(p => p.StudentId == studentId)
                                      .SortByDescending(p => p.CreatedAtUtc)
                                      .Limit(take)
                                      .ToListAsync(cancellationToken)
                                      .ConfigureAwait(false);
        return [.. documents.Select(ToDomain)];
    }

    private static PaymentDocument ToDocument(Payment payment, long version) => new()
    {
        Id = payment.EnrollmentId,
        StudentId = payment.StudentId,
        CourseId = payment.CourseId,
        AmountInCents = payment.AmountInCents,
        Status = payment.Status,
        ProviderReference = payment.ProviderReference,
        DeclineReason = payment.DeclineReason,
        CreatedAtUtc = payment.CreatedAt.UtcDateTime,
        RefundedAtUtc = payment.RefundedAt?.UtcDateTime,
        Version = version,
    };

    private static Payment ToDomain(PaymentDocument d) =>
        Payment.Rehydrate(d.Id, d.StudentId, d.CourseId, d.AmountInCents, d.Status, d.ProviderReference, d.DeclineReason,
                          AsUtc(d.CreatedAtUtc), d.RefundedAtUtc is { } refunded ? AsUtc(refunded) : null, d.Version);

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>Indexes of the Billing database, created idempotently at startup by the host.</summary>
public static class BillingIndexes
{
    public static Task EnsureAsync(IMongoClientFactory<DatabaseContext> clientFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        return clientFactory.GetCollection<PaymentDocument>().Indexes.CreateOneAsync(
            new CreateIndexModel<PaymentDocument>(
                Builders<PaymentDocument>.IndexKeys.Ascending(p => p.StudentId).Descending(p => p.CreatedAtUtc),
                new CreateIndexOptions { Name = "student_payments" }),
            cancellationToken: cancellationToken);
    }
}
