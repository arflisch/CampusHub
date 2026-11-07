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

/// <summary>Raised on every change of status: the student's screens follow the saga live (SignalR).</summary>
public sealed record EnrollmentStatusChanged(
    Guid EnrollmentId,
    Guid CourseId,
    string StudentId,
    EnrollmentStatus Status,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Raised once, when a confirmed enrollment is cancelled (by the student or because the course was cancelled).</summary>
public sealed record EnrollmentCancelled(
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    string StudentId,
    string Reason,
    int RefundedInCents,
    DateTimeOffset OccurredAt) : IDomainEvent;

/// <summary>Raised once per confirmed enrollment, shortly before the course starts.</summary>
public sealed record ClassReminderDue(
    Guid EnrollmentId,
    Guid CourseId,
    string CourseTitle,
    DateTimeOffset CourseStartsAt,
    string StudentId,
    DateTimeOffset OccurredAt) : IDomainEvent;
