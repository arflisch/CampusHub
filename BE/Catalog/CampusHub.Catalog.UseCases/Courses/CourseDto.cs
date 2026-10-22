namespace Dev4u.CampusHub.Catalog.UseCases.Courses;

/// <summary>Course as shown to the UIs.</summary>
public sealed record CourseDto(
    Guid Id,
    string Code,
    string Title,
    string TeacherId,
    int Capacity,
    int SeatsLeft,
    string Status,
    DateTimeOffset? PublishedAt);

/// <summary>Seat reservation as returned to the enrollment saga.</summary>
public sealed record SeatReservationDto(
    Guid ReservationId,
    Guid CourseId,
    string CourseTitle,
    string StudentId,
    string Status,
    DateTimeOffset HeldUntil);
