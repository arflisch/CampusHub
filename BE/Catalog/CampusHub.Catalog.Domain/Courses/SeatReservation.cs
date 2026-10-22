using FluentResults;

namespace Dev4u.CampusHub.Catalog.Domain.Courses;

/// <summary>
/// A seat held for a student while the enrollment saga runs. Holds expire on their own, so a saga that dies
/// half-way never blocks a seat forever.
/// </summary>
public sealed class SeatReservation
{
    private SeatReservation(Guid id, string studentId, string idempotencyKey, ReservationStatus status,
                            DateTimeOffset createdAt, DateTimeOffset heldUntil)
    {
        Id = id;
        StudentId = studentId;
        IdempotencyKey = idempotencyKey;
        Status = status;
        CreatedAt = createdAt;
        HeldUntil = heldUntil;
    }

    public Guid Id { get; }

    /// <summary>Identity provider subject of the student.</summary>
    public string StudentId { get; }

    /// <summary>Key sent by the caller so that a retried reservation returns the same seat.</summary>
    public string IdempotencyKey { get; }

    public ReservationStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset HeldUntil { get; }

    /// <summary>True while the reservation takes a seat.</summary>
    public bool Occupies(DateTimeOffset now) =>
        Status == ReservationStatus.Confirmed || (Status == ReservationStatus.Held && HeldUntil > now);

    internal static SeatReservation Hold(Guid id, string studentId, string idempotencyKey, DateTimeOffset now, TimeSpan holdDuration) =>
        new(id, studentId, idempotencyKey, ReservationStatus.Held, now, now + holdDuration);

    /// <summary>Rebuilds a reservation from storage. Persistence only.</summary>
    public static SeatReservation Rehydrate(Guid id, string studentId, string idempotencyKey, ReservationStatus status,
                                            DateTimeOffset createdAt, DateTimeOffset heldUntil) =>
        new(id, studentId, idempotencyKey, status, createdAt, heldUntil);

    internal Result Confirm(DateTimeOffset now)
    {
        switch (Status)
        {
            case ReservationStatus.Confirmed:
                return Result.Ok(); // retried call
            case ReservationStatus.Released:
                return Result.Fail(CourseErrors.ReservationReleased(Id));
            case ReservationStatus.Held when HeldUntil <= now:
                return Result.Fail(CourseErrors.ReservationExpired(Id));
            default:
                Status = ReservationStatus.Confirmed;
                return Result.Ok();
        }
    }

    internal void Release() => Status = ReservationStatus.Released;
}
