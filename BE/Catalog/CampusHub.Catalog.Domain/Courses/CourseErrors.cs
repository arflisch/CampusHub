using Arc4u.Results;
using Dev4u.CampusHub.Errors;

namespace Dev4u.CampusHub.Catalog.Domain.Courses;

/// <summary>Every failure the Catalog domain can return, with its stable code.</summary>
public static class CourseErrors
{
    public const string ConcurrencyConflictCode = "course.concurrency_conflict";

    public static ProblemDetailError InvalidCode() =>
        AppErrors.Validation("course.invalid_code", "The course code must contain between 3 and 20 characters.");

    public static ProblemDetailError InvalidTitle() =>
        AppErrors.Validation("course.invalid_title", "The course title is required and limited to 200 characters.");

    public static ProblemDetailError InvalidCapacity() =>
        AppErrors.Validation("course.invalid_capacity", $"The capacity must be between 1 and {Course.MaxCapacity}.");

    public static ProblemDetailError InvalidPrice() =>
        AppErrors.Validation("course.invalid_price", $"The price must be between 0 and {Course.MaxPriceInCents / 100} EUR.");

    public static ProblemDetailError InvalidSchedule(string detail) =>
        AppErrors.Validation("course.invalid_schedule", detail);

    public static ProblemDetailError Started(Guid courseId) =>
        AppErrors.BusinessRule("course.started", $"Course '{courseId}' has already started: enrollments are closed.");

    public static ProblemDetailError NotFound(Guid courseId) =>
        AppErrors.NotFound("course.not_found", $"Course '{courseId}' was not found.");

    public static ProblemDetailError CodeTaken(string code) =>
        AppErrors.Conflict("course.code_taken", $"A course with code '{code}' already exists.");

    public static ProblemDetailError NotOwner(Guid courseId) =>
        AppErrors.Forbidden("course.not_owner", $"Only the teacher of course '{courseId}' can change it.");

    public static ProblemDetailError InvalidTransition(CourseStatus from, CourseStatus to) =>
        AppErrors.Conflict("course.invalid_transition", $"A course cannot go from {from} to {to}.");

    public static ProblemDetailError NotOpen(Guid courseId) =>
        AppErrors.BusinessRule("course.not_open", $"Course '{courseId}' is not open for enrollment.");

    public static ProblemDetailError Full(Guid courseId) =>
        AppErrors.Conflict("course.full", $"Course '{courseId}' has no seat left.");

    public static ProblemDetailError AlreadyEnrolled(Guid courseId) =>
        AppErrors.Conflict("course.already_enrolled", $"The student already holds a seat in course '{courseId}'.");

    public static ProblemDetailError ReservationNotFound(Guid reservationId) =>
        AppErrors.NotFound("reservation.not_found", $"Seat reservation '{reservationId}' was not found.");

    public static ProblemDetailError ReservationExpired(Guid reservationId) =>
        AppErrors.Conflict("reservation.expired", $"Seat reservation '{reservationId}' expired before it was confirmed.");

    public static ProblemDetailError ReservationReleased(Guid reservationId) =>
        AppErrors.Conflict("reservation.released", $"Seat reservation '{reservationId}' was released.");

    public static ProblemDetailError ConcurrencyConflict(Guid courseId) =>
        AppErrors.Concurrency(ConcurrencyConflictCode, $"Course '{courseId}' was modified concurrently.");
}
