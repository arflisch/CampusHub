namespace Dev4u.CampusHub.Enrollment.IntegrationEvents;

/// <summary>Topic and CloudEvent types published by the Enrollment service.</summary>
public static class EnrollmentEvents
{
    /// <summary>One topic per publishing service; subscribers route on the CloudEvent type.</summary>
    public const string Topic = "campushub.enrollment";

    /// <summary>CloudEvent type of <see cref="EnrollmentConfirmedV1"/>, matched by the Dapr subscriptions.</summary>
    public const string EnrollmentConfirmedV1Type = "campushub.enrollment.enrollment.confirmed";
}

/// <summary>
/// A student's enrollment reached its final, confirmed state.
/// </summary>
/// <param name="EventId">Unique id of the event, also the CloudEvent id: subscribers deduplicate on it.</param>
/// <param name="OccurredAt">When the enrollment was confirmed (UTC).</param>
/// <param name="EnrollmentId">The confirmed enrollment.</param>
/// <param name="CourseId">The course the student joined.</param>
/// <param name="CourseTitle">Title of the course at confirmation time (event-carried state).</param>
/// <param name="StudentId">Identity provider subject of the student.</param>
public sealed record EnrollmentConfirmedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    string StudentId);
