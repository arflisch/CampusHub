namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>Progress of an enrollment through the saga.</summary>
public enum EnrollmentStatus
{
    /// <summary>Request accepted, the saga has not reserved a seat yet.</summary>
    Requested = 0,

    /// <summary>A seat is held in Catalog for this student.</summary>
    SeatHeld = 1,

    /// <summary>Pivot reached: the seat is confirmed and the enrollment is final.</summary>
    Confirmed = 2,

    /// <summary>Refused (course full, not open...) or compensated after a failure.</summary>
    Rejected = 3,
}
