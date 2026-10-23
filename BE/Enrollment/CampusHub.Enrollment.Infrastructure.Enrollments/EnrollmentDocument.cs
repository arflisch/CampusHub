using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

/// <summary>Storage shape of a <see cref="StudentEnrollment"/>; the domain stays free of persistence attributes.</summary>
[BsonIgnoreExtraElements]
public sealed class EnrollmentDocument
{
    [BsonId]
    public Guid Id { get; set; }

    public Guid CourseId { get; set; }

    public string StudentId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public EnrollmentStatus Status { get; set; }

    public Guid? ReservationId { get; set; }

    public string? CourseTitle { get; set; }

    public DateTime? CourseStartsAtUtc { get; set; }

    public DateTime? CourseEndsAtUtc { get; set; }

    /// <summary>Missing in enrollments stored before prices existed: read as 0 (free).</summary>
    public int CoursePriceInCents { get; set; }

    public string? RejectionCode { get; set; }

    public DateTime? ReminderSentAtUtc { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public long Version { get; set; }
}

internal static class EnrollmentDocumentMapping
{
    public static EnrollmentDocument ToDocument(this StudentEnrollment enrollment, long version) => new()
    {
        Id = enrollment.Id,
        CourseId = enrollment.CourseId,
        StudentId = enrollment.StudentId,
        Status = enrollment.Status,
        ReservationId = enrollment.ReservationId,
        CourseTitle = enrollment.CourseTitle,
        CourseStartsAtUtc = enrollment.CourseStartsAt?.UtcDateTime,
        CourseEndsAtUtc = enrollment.CourseEndsAt?.UtcDateTime,
        CoursePriceInCents = enrollment.CoursePriceInCents,
        RejectionCode = enrollment.RejectionCode,
        ReminderSentAtUtc = enrollment.ReminderSentAt?.UtcDateTime,
        RequestedAtUtc = enrollment.RequestedAt.UtcDateTime,
        UpdatedAtUtc = enrollment.UpdatedAt.UtcDateTime,
        Version = version,
    };

    public static StudentEnrollment ToDomain(this EnrollmentDocument document) =>
        StudentEnrollment.Rehydrate(document.Id, document.CourseId, document.StudentId, document.Status, document.ReservationId,
                                    document.CourseTitle, AsUtc(document.CourseStartsAtUtc), AsUtc(document.CourseEndsAtUtc),
                                    document.CoursePriceInCents, document.RejectionCode, AsUtc(document.ReminderSentAtUtc), AsUtc(document.RequestedAtUtc),
                                    AsUtc(document.UpdatedAtUtc), document.Version);

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? AsUtc(DateTime? value) => value is { } date ? AsUtc(date) : null;
}
