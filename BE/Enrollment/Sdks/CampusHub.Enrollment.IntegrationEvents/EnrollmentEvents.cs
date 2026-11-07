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

    /// <summary>CloudEvent type of <see cref="EnrollmentCancelledV1"/>.</summary>
    public const string EnrollmentCancelledV1Type = "campushub.enrollment.enrollment.cancelled";

    /// <summary>CloudEvent type of <see cref="EnrollmentStatusChangedV1"/>.</summary>
    public const string EnrollmentStatusChangedV1Type = "campushub.enrollment.enrollment.status_changed";
}

/// <summary>A confirmed enrollment was cancelled and its money given back.</summary>
/// <param name="EventId">Unique id of the event, also the CloudEvent id: subscribers deduplicate on it.</param>
/// <param name="OccurredAt">When the enrollment was cancelled (UTC).</param>
/// <param name="EnrollmentId">The cancelled enrollment.</param>
/// <param name="CourseId">The course.</param>
/// <param name="CourseTitle">Title of the course (event-carried state).</param>
/// <param name="StudentId">Identity provider subject of the student.</param>
/// <param name="Reason"><c>enrollment.cancelled_by_student</c> or <c>course.cancelled</c>.</param>
/// <param name="RefundedInCents">Amount given back (EUR cents); 0 for a free course.</param>
public sealed record EnrollmentCancelledV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    string StudentId,
    string Reason,
    int RefundedInCents);

/// <summary>
/// The status of an enrollment changed (Requested, SeatHeld, Paid, Confirmed, Rejected, Cancelled): lets the
/// student's screens follow the saga live. Informative only, never a command.
/// </summary>
/// <param name="EventId">Unique id of the event, also the CloudEvent id: subscribers deduplicate on it.</param>
/// <param name="OccurredAt">When the status changed (UTC).</param>
/// <param name="EnrollmentId">The enrollment.</param>
/// <param name="CourseId">The course.</param>
/// <param name="StudentId">Identity provider subject of the student.</param>
/// <param name="Status">The new status.</param>
public sealed record EnrollmentStatusChangedV1(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid EnrollmentId,
    Guid CourseId,
    string StudentId,
    string Status);

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
