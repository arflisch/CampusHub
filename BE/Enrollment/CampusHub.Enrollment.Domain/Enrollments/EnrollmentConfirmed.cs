using Dev4u.CampusHub.Events;

namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>Raised once, when an enrollment reaches the Confirmed state.</summary>
public sealed record EnrollmentConfirmed(
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    string StudentId,
    DateTimeOffset OccurredAt) : IDomainEvent;
