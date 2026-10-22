using Dev4u.CampusHub.Catalog.Domain.Courses;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Dev4u.CampusHub.Catalog.Infrastructure.Courses;

/// <summary>
/// Storage shape of a <see cref="Course"/>. Kept separate from the aggregate so the domain stays free of any
/// persistence attribute and the stored schema can evolve on its own.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class CourseDocument
{
    [BsonId]
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string TeacherId { get; set; } = string.Empty;

    public int Capacity { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime EndsAtUtc { get; set; }

    [BsonRepresentation(BsonType.String)]
    public CourseStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? PublishedAtUtc { get; set; }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002", Justification = "Storage DTO deserialized by the MongoDB driver.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2227", Justification = "Storage DTO deserialized by the MongoDB driver.")]
    public List<SeatReservationDocument> Reservations { get; set; } = [];

    public long Version { get; set; }
}

[BsonIgnoreExtraElements]
public sealed class SeatReservationDocument
{
    public Guid Id { get; set; }

    public string StudentId { get; set; } = string.Empty;

    public string IdempotencyKey { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public ReservationStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime HeldUntilUtc { get; set; }
}

internal static class CourseDocumentMapping
{
    public static CourseDocument ToDocument(this Course course, long version) => new()
    {
        Id = course.Id,
        Code = course.Code,
        Title = course.Title,
        TeacherId = course.TeacherId,
        Capacity = course.Capacity,
        StartsAtUtc = course.Schedule.StartsAt.UtcDateTime,
        EndsAtUtc = course.Schedule.EndsAt.UtcDateTime,
        Status = course.Status,
        CreatedAtUtc = course.CreatedAt.UtcDateTime,
        PublishedAtUtc = course.PublishedAt?.UtcDateTime,
        Reservations = [.. course.Reservations.Select(r => new SeatReservationDocument
        {
            Id = r.Id,
            StudentId = r.StudentId,
            IdempotencyKey = r.IdempotencyKey,
            Status = r.Status,
            CreatedAtUtc = r.CreatedAt.UtcDateTime,
            HeldUntilUtc = r.HeldUntil.UtcDateTime,
        })],
        Version = version,
    };

    public static Course ToDomain(this CourseDocument document) =>
        Course.Rehydrate(
            document.Id,
            document.Code,
            document.Title,
            document.TeacherId,
            document.Capacity,
            CourseSchedule.Rehydrate(AsUtc(document.StartsAtUtc), AsUtc(document.EndsAtUtc)),
            document.Status,
            AsUtc(document.CreatedAtUtc),
            document.PublishedAtUtc is { } published ? AsUtc(published) : null,
            document.Reservations.Select(r => SeatReservation.Rehydrate(
                r.Id, r.StudentId, r.IdempotencyKey, r.Status, AsUtc(r.CreatedAtUtc), AsUtc(r.HeldUntilUtc))),
            document.Version);

    private static DateTimeOffset AsUtc(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
