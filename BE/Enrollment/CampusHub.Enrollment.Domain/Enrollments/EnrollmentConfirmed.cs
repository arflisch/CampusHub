using Dev4u.CampusHub.Events;

namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>Raised once, when an enrollment reaches the Confirmed state.</summary>
public sealed record EnrollmentConfirmed(
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    DateTimeOffset CourseStartsAt,
    DateTimeOffset CourseEndsAt,
    string StudentId,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Raised once per confirmed enrollment, shortly before the course starts.</summary>
public sealed record ClassReminderDue(
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    DateTimeOffset CourseStartsAt,
    string StudentId,
    DateTimeOffset OccurredAt) : IDomainEvent;
