namespace Dev4u.CampusHub.Catalog.Domain.Courses;

/// <summary>State of a seat reservation taken by the enrollment saga.</summary>
public enum ReservationStatus
{
    /// <summary>The seat is held until <see cref="SeatReservation.HeldUntil"/>; it frees itself when the hold expires.</summary>
    Held = 0,

    /// <summary>The enrollment is confirmed: the seat is taken for good.</summary>
    Confirmed = 1,

    /// <summary>The seat was given back (compensation or cancellation).</summary>
    Released = 2,
}
