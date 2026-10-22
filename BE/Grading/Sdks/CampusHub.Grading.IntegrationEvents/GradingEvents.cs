namespace Dev4u.CampusHub.Grading.IntegrationEvents;

/// <summary>Topic and CloudEvent types published by the Grading service.</summary>
public static class GradingEvents
{
    /// <summary>One topic per publishing service; subscribers route on the CloudEvent type.</summary>
    public const string Topic = "campushub.grading";

    /// <summary>CloudEvent type of <see cref="CertificateIssuedV1"/>, matched by the Dapr subscriptions.</summary>
    public const string CertificateIssuedV1Type = "campushub.grading.certificate.issued";
}

/// <summary>
/// A student received the certificate of a course that ended.
/// </summary>
/// <param name="EventId">Unique id of the event, also the CloudEvent id: subscribers deduplicate on it.</param>
/// <param name="OccurredAt">When the certificate was issued (UTC).</param>
/// <param name="CertificateNumber">Human-readable, unique certificate number.</param>
/// <param name="EnrollmentId">The enrollment the certificate closes.</param>
/// <param name="CourseId">The course.</param>
/// <param name="CourseTitle">Title of the course (event-carried state).</param>
/// <param name="StudentId">Identity provider subject of the student.</param>
public sealed record CertificateIssuedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    string CertificateNumber,
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    string StudentId);
