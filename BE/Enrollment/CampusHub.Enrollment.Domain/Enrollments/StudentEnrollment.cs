using Dev4u.CampusHub.Events;
using FluentResults;

namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>
/// A student's request to join a course, driven to completion by the enrollment saga.
/// <para>
/// The id is chosen by the client (its idempotency key): sending the same request twice returns the same
/// enrollment instead of creating a second one. Every transition is idempotent as well, because the saga's
/// activities run at least once. The aggregate records <see cref="EnrollmentConfirmed"/> when it reaches the
/// pivot; the repository writes it to the outbox in the same transaction as the state change.
/// </para>
/// </summary>
public sealed class StudentEnrollment
{
    private readonly List<IDomainEvent> _domainEvents = [];

    private StudentEnrollment(Guid id, Guid courseId, string studentId, EnrollmentStatus status, Guid? reservationId,
                       string? courseTitle, string? rejectionCode, DateTimeOffset requestedAt, DateTimeOffset updatedAt, long version)
    {
        Id = id;
        CourseId = courseId;
        StudentId = studentId;
        Status = status;
        ReservationId = reservationId;
        CourseTitle = courseTitle;
        RejectionCode = rejectionCode;
        RequestedAt = requestedAt;
        UpdatedAt = updatedAt;
        Version = version;
    }

    public Guid Id { get; }

    public Guid CourseId { get; }

    /// <summary>Identity provider subject of the student.</summary>
    public string StudentId { get; }

    public EnrollmentStatus Status { get; private set; }

    /// <summary>Seat held in Catalog, once the saga reserved it.</summary>
    public Guid? ReservationId { get; private set; }

    /// <summary>Course title copied from Catalog when the seat is reserved (event-carried state for notifications).</summary>
    public string? CourseTitle { get; private set; }

    /// <summary>Code of the reason when the enrollment was rejected (e.g. course.full).</summary>
    public string? RejectionCode { get; private set; }

    public DateTimeOffset RequestedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented by the repository on every successful save.</summary>
    public long Version { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public static Result<StudentEnrollment> Request(Guid id, Guid courseId, string studentId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);

        if (id == Guid.Empty)
        {
            return Result.Fail<StudentEnrollment>(EnrollmentErrors.InvalidRequest("A request id is required."));
        }

        if (courseId == Guid.Empty)
        {
            return Result.Fail<StudentEnrollment>(EnrollmentErrors.InvalidRequest("A course id is required."));
        }

        return Result.Ok(new StudentEnrollment(id, courseId, studentId, EnrollmentStatus.Requested, reservationId: null,
                                        courseTitle: null, rejectionCode: null, now, now, version: 0));
    }

    /// <summary>Rebuilds an enrollment from storage. Persistence only: no rule is checked.</summary>
    public static StudentEnrollment Rehydrate(Guid id, Guid courseId, string studentId, EnrollmentStatus status, Guid? reservationId,
                                       string? courseTitle, string? rejectionCode, DateTimeOffset requestedAt, DateTimeOffset updatedAt, long version) =>
        new(id, courseId, studentId, status, reservationId, courseTitle, rejectionCode, requestedAt, updatedAt, version);

    public bool BelongsTo(string studentId) => string.Equals(StudentId, studentId, StringComparison.Ordinal);

    /// <summary>Idempotency key sent to Catalog: a retried reservation gets the same seat.</summary>
    public string SeatIdempotencyKey => $"enrollment:{Id}";

    public Result MarkSeatReserved(Guid reservationId, string courseTitle, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courseTitle);

        if (Status == EnrollmentStatus.SeatHeld && ReservationId == reservationId)
        {
            return Result.Ok(); // replayed activity
        }

        if (Status != EnrollmentStatus.Requested)
        {
            return Result.Fail(EnrollmentErrors.InvalidTransition(Status, EnrollmentStatus.SeatHeld));
        }

        Status = EnrollmentStatus.SeatHeld;
        ReservationId = reservationId;
        CourseTitle = courseTitle;
        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>Pivot of the saga: after this, the enrollment is never compensated.</summary>
    public Result Confirm(DateTimeOffset now)
    {
        if (Status == EnrollmentStatus.Confirmed)
        {
            return Result.Ok(); // replayed activity: no second event
        }

        if (Status != EnrollmentStatus.SeatHeld || ReservationId is null || CourseTitle is null)
        {
            return Result.Fail(EnrollmentErrors.InvalidTransition(Status, EnrollmentStatus.Confirmed));
        }

        Status = EnrollmentStatus.Confirmed;
        UpdatedAt = now;
        _domainEvents.Add(new EnrollmentConfirmed(Id, CourseId, CourseTitle, StudentId, now));
        return Result.Ok();
    }

    public Result Reject(string reasonCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        if (Status == EnrollmentStatus.Rejected)
        {
            return Result.Ok(); // replayed compensation
        }

        if (Status == EnrollmentStatus.Confirmed)
        {
            return Result.Fail(EnrollmentErrors.InvalidTransition(Status, EnrollmentStatus.Rejected));
        }

        Status = EnrollmentStatus.Rejected;
        RejectionCode = reasonCode;
        UpdatedAt = now;
        return Result.Ok();
    }

    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Called by the repository once the new version is stored. Persistence only.</summary>
    public void MarkPersisted() => Version++;
}
