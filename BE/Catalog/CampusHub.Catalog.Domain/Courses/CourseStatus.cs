namespace Dev4u.CampusHub.Catalog.Domain.Courses;

/// <summary>Lifecycle of a course.</summary>
public enum CourseStatus
{
    Draft = 0,
    Published = 1,
    Closed = 2,

    /// <summary>Cancelled by its teacher before it started: the enrolled students are refunded.</summary>
    Cancelled = 3,
}

/// <summary>
/// When a course was cancelled, and when the other services were told (CourseCancelled event). Kept on the course:
/// the announcement is retried until it succeeds, so it can never be lost between the save and the publication.
/// </summary>
public sealed record CourseCancellation(DateTimeOffset CancelledAt, DateTimeOffset? AnnouncedAt);
