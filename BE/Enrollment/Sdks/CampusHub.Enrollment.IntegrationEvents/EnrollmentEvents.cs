namespace Dev4u.CampusHub.Enrollment.IntegrationEvents;

/// <summary>Topic and CloudEvent types published by the Enrollment service.</summary>
public static class EnrollmentEvents
{
    /// <summary>One topic per publishing service; subscribers route on the CloudEvent type.</summary>
    public const string Topic = "campushub.enrollment";

    /// <summary>CloudEvent type of <see cref="EnrollmentConfirmedV1"/>, matched by the Dapr subscriptions.</summary>
    public const string EnrollmentConfirmedV1Type = "campushub.enrollment.enrollment.confirmed";

    /// <summary>CloudEvent type of <see cref="ClassReminderDueV1"/>.</summary>
    public const string ClassReminderDueV1Type = "campushub.enrollment.class.reminder_due";
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
/// <param name="CourseStartsAt">When the course starts. Added later in V1 (optional field): null in older events.</param>
/// <param name="CourseEndsAt">When the course ends. Added later in V1 (optional field): null in older events.</param>
public sealed record EnrollmentConfirmedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    string StudentId,
    DateTimeOffset? CourseStartsAt = null,
    DateTimeOffset? CourseEndsAt = null);

/// <summary>
/// A course a student is enrolled in starts soon (published once per enrollment, about a day before).
/// </summary>
/// <param name="EventId">Unique id of the event, also the CloudEvent id: subscribers deduplicate on it.</param>
/// <param name="OccurredAt">When the reminder was decided (UTC).</param>
/// <param name="EnrollmentId">The confirmed enrollment.</param>
/// <param name="CourseId">The course.</param>
/// <param name="CourseTitle">Title of the course (event-carried state).</param>
/// <param name="CourseStartsAt">When the course starts.</param>
/// <param name="StudentId">Identity provider subject of the student.</param>
public sealed record ClassReminderDueV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    DateTimeOffset CourseStartsAt,
    string StudentId);
