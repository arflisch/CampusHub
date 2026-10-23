using FluentResults;

namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>A seat held in Catalog for the enrollment, with the course details the enrollment keeps.</summary>
/// <param name="CoursePriceInCents">Price of the course at reservation time; 0 for a free course.</param>
public sealed record SeatHold(Guid ReservationId, string CourseTitle, DateTimeOffset CourseStartsAt, DateTimeOffset CourseEndsAt,
                              int CoursePriceInCents = 0);

/// <summary>
/// Port to the Catalog service (anti-corruption layer). Failures keep the Catalog error codes; failures that are
/// worth retrying (Catalog down, timeout, concurrency) are flagged transient.
/// </summary>
public interface ICourseSeats
{
    Task<Result<SeatHold>> ReserveAsync(Guid courseId, string studentId, string idempotencyKey, CancellationToken cancellationToken);

    Task<Result> ConfirmAsync(Guid courseId, Guid reservationId, CancellationToken cancellationToken);

    Task<Result> ReleaseAsync(Guid courseId, Guid reservationId, CancellationToken cancellationToken);
}
