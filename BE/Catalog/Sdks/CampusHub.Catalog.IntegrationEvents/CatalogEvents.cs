namespace Dev4u.CampusHub.Catalog.IntegrationEvents;

/// <summary>Topic and CloudEvent types published by the Catalog service.</summary>
public static class CatalogEvents
{
    /// <summary>One topic per publishing service; subscribers route on the CloudEvent type.</summary>
    public const string Topic = "campushub.catalog";

    /// <summary>CloudEvent type of <see cref="CourseCancelledV1"/>, matched by the Dapr subscriptions.</summary>
    public const string CourseCancelledV1Type = "campushub.catalog.course.cancelled";
}

/// <summary>A teacher cancelled a course before it started: its students must be refunded and told.</summary>
/// <param name="EventId">Unique id of the event, also the CloudEvent id: subscribers deduplicate on it.</param>
/// <param name="OccurredAt">When the course was cancelled (UTC).</param>
/// <param name="CourseId">The cancelled course.</param>
/// <param name="CourseTitle">Title of the course (event-carried state).</param>
public sealed record CourseCancelledV1(Guid EventId, DateTimeOffset OccurredAt, Guid CourseId, string CourseTitle);
