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
                       string? courseTitle, DateTimeOffset? courseStartsAt, DateTimeOffset? courseEndsAt, int coursePriceInCents, string? rejectionCode,
                       DateTimeOffset? reminderSentAt, DateTimeOffset requestedAt, DateTimeOffset updatedAt, long version,
                       string? studentName = null, string? courseTeacherId = null, string? cancellationReason = null)
    {
        Id = id;
        CourseId = courseId;
        StudentId = studentId;
        Status = status;
        ReservationId = reservationId;
        CourseTitle = courseTitle;
        CourseStartsAt = courseStartsAt;
        CourseEndsAt = courseEndsAt;
        CoursePriceInCents = coursePriceInCents;
        RejectionCode = rejectionCode;
        ReminderSentAt = reminderSentAt;
        StudentName = studentName;
        CourseTeacherId = courseTeacherId;
        CancellationReason = cancellationReason;
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

    /// <summary>When the course starts, copied from Catalog with the title (reminders, certificates).</summary>
    public DateTimeOffset? CourseStartsAt { get; private set; }

    public DateTimeOffset? CourseEndsAt { get; private set; }

    /// <summary>Price of the course, copied from Catalog with the seat; 0 for a free course (no payment step).</summary>
    public int CoursePriceInCents { get; private set; }

    public bool RequiresPayment => CoursePriceInCents > 0;

    /// <summary>Display name of the student when the request was made (shown to the teacher); may be unknown.</summary>
    public string? StudentName { get; }

    /// <summary>Teacher of the course, copied with the seat: who may see this enrollment as a participant.</summary>
    public string? CourseTeacherId { get; private set; }

    /// <summary>Why a confirmed enrollment was cancelled (<see cref="CancelledByStudent"/> or <see cref="CancelledWithCourse"/>).</summary>
    public string? CancellationReason { get; private set; }

    public const string CancelledByStudent = "enrollment.cancelled_by_student";
    public const string CancelledWithCourse = "course.cancelled";

    /// <summary>When the student was reminded that the course starts soon; null until then.</summary>
    public DateTimeOffset? ReminderSentAt { get; private set; }

    /// <summary>Code of the reason when the enrollment was rejected (e.g. course.full).</summary>
    public string? RejectionCode { get; private set; }

    public DateTimeOffset RequestedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented by the repository on every successful save.</summary>
    public long Version { get; private set; }

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    public static Result<StudentEnrollment> Request(Guid id, Guid courseId, string studentId, DateTimeOffset now, string? studentName = null)
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

        var enrollment = new StudentEnrollment(id, courseId, studentId, EnrollmentStatus.Requested, reservationId: null,
                                               courseTitle: null, courseStartsAt: null, courseEndsAt: null, coursePriceInCents: 0,
                                               rejectionCode: null, reminderSentAt: null, now, now, version: 0,
                                               string.IsNullOrWhiteSpace(studentName) ? null : studentName.Trim());
        enrollment.StatusChanged(now);
        return Result.Ok(enrollment);
    }

    /// <summary>Rebuilds an enrollment from storage. Persistence only: no rule is checked.</summary>
    public static StudentEnrollment Rehydrate(Guid id, Guid courseId, string studentId, EnrollmentStatus status, Guid? reservationId,
                                       string? courseTitle, DateTimeOffset? courseStartsAt, DateTimeOffset? courseEndsAt,
                                       int coursePriceInCents, string? rejectionCode, DateTimeOffset? reminderSentAt,
                                       DateTimeOffset requestedAt, DateTimeOffset updatedAt, long version,
                                       string? studentName = null, string? courseTeacherId = null, string? cancellationReason = null) =>
        new(id, courseId, studentId, status, reservationId, courseTitle, courseStartsAt, courseEndsAt, coursePriceInCents,
            rejectionCode, reminderSentAt, requestedAt, updatedAt, version, studentName, courseTeacherId, cancellationReason);

    public bool BelongsTo(string studentId) => string.Equals(StudentId, studentId, StringComparison.Ordinal);

    /// <summary>Idempotency key sent to Catalog: a retried reservation gets the same seat.</summary>
    public string SeatIdempotencyKey => $"enrollment:{Id}";

    public Result MarkSeatReserved(SeatHold seat, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(seat);
        ArgumentException.ThrowIfNullOrWhiteSpace(seat.CourseTitle);

        if (Status == EnrollmentStatus.SeatHeld && ReservationId == seat.ReservationId)
        {
            return Result.Ok(); // replayed activity
        }

        if (Status != EnrollmentStatus.Requested)
        {
            return Result.Fail(EnrollmentErrors.InvalidTransition(Status, EnrollmentStatus.SeatHeld));
        }

        Status = EnrollmentStatus.SeatHeld;
        ReservationId = seat.ReservationId;
        CourseTitle = seat.CourseTitle;
        CourseStartsAt = seat.CourseStartsAt;
        CourseEndsAt = seat.CourseEndsAt;
        CoursePriceInCents = seat.CoursePriceInCents;
        CourseTeacherId = seat.CourseTeacherId;
        UpdatedAt = now;
        StatusChanged(now);
        return Result.Ok();
    }

    /// <summary>Records that the course was paid (paid courses only). Replaying it succeeds without change.</summary>
    public Result MarkPaid(DateTimeOffset now)
    {
        if (Status == EnrollmentStatus.Paid)
        {
            return Result.Ok(); // replayed activity
        }

        if (Status != EnrollmentStatus.SeatHeld || !RequiresPayment)
        {
            return Result.Fail(EnrollmentErrors.InvalidTransition(Status, EnrollmentStatus.Paid));
        }

        Status = EnrollmentStatus.Paid;
        UpdatedAt = now;
        StatusChanged(now);
        return Result.Ok();
    }

    /// <summary>
    /// Pivot of the saga: after this, the enrollment is never compensated. A paid course must be paid first; a free
    /// one goes straight from SeatHeld.
    /// </summary>
    public Result Confirm(DateTimeOffset now)
    {
        if (Status == EnrollmentStatus.Confirmed)
        {
            return Result.Ok(); // replayed activity: no second event
        }

        var readyForPivot = RequiresPayment ? Status == EnrollmentStatus.Paid : Status == EnrollmentStatus.SeatHeld;
        if (!readyForPivot || ReservationId is null || CourseTitle is null
            || CourseStartsAt is not { } startsAt || CourseEndsAt is not { } endsAt)
        {
            return Result.Fail(EnrollmentErrors.InvalidTransition(Status, EnrollmentStatus.Confirmed));
        }

        Status = EnrollmentStatus.Confirmed;
        UpdatedAt = now;
        _domainEvents.Add(new EnrollmentConfirmed(Id, CourseId, CourseTitle, startsAt, endsAt, StudentId, now));
        StatusChanged(now);
        return Result.Ok();
    }

    /// <summary>
    /// Reminds the student that the course starts soon. Once per enrollment: a second call (job rerun, retry)
    /// succeeds without raising a second <see cref="ClassReminderDue"/>.
    /// </summary>
    public Result RemindOfClass(DateTimeOffset now)
    {
        if (ReminderSentAt is not null)
        {
            return Result.Ok();
        }

        if (Status != EnrollmentStatus.Confirmed || CourseTitle is null || CourseStartsAt is not { } startsAt)
        {
            return Result.Fail(EnrollmentErrors.NotConfirmed(Id));
        }

        ReminderSentAt = now;
        UpdatedAt = now;
        _domainEvents.Add(new ClassReminderDue(Id, CourseId, CourseTitle, startsAt, StudentId, now));
        return Result.Ok();
    }

    public Result Reject(string reasonCode, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reasonCode);

        if (Status == EnrollmentStatus.Rejected)
        {
            return Result.Ok(); // replayed compensation
        }

        if (Status is EnrollmentStatus.Confirmed or EnrollmentStatus.Cancelled)
        {
            return Result.Fail(EnrollmentErrors.InvalidTransition(Status, EnrollmentStatus.Rejected));
        }

        Status = EnrollmentStatus.Rejected;
        RejectionCode = reasonCode;
        UpdatedAt = now;
        StatusChanged(now);
        return Result.Ok();
    }

    /// <summary>
    /// Cancels a confirmed enrollment, once its money was given back (the caller refunds first). Cancelling twice
    /// succeeds without a second event. A student may cancel only before the course starts; a cancelled course
    /// cancels its enrollments regardless.
    /// </summary>
    public Result Cancel(string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status == EnrollmentStatus.Cancelled)
        {
            return Result.Ok();
        }

        if (Status != EnrollmentStatus.Confirmed || CourseTitle is null)
        {
            return Result.Fail(EnrollmentErrors.NotCancellable(Id));
        }

        if (reason == CancelledByStudent && CourseStartsAt is { } startsAt && now >= startsAt)
        {
            return Result.Fail(EnrollmentErrors.CourseStarted(Id));
        }

        Status = EnrollmentStatus.Cancelled;
        CancellationReason = reason;
        UpdatedAt = now;
        _domainEvents.Add(new EnrollmentCancelled(Id, CourseId, CourseTitle, StudentId, reason, CoursePriceInCents, now));
        StatusChanged(now);
        return Result.Ok();
    }

    private void StatusChanged(DateTimeOffset now) =>
        _domainEvents.Add(new EnrollmentStatusChanged(Id, CourseId, StudentId, Status, now));

    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>Called by the repository once the new version is stored. Persistence only.</summary>
    public void MarkPersisted() => Version++;
}
