using Arc4u.Results;
using Dev4u.CampusHub.Errors;

namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>Every failure the Enrollment domain can return, with its stable code.</summary>
public static class EnrollmentErrors
{
    public const string ConcurrencyConflictCode = "enrollment.concurrency_conflict";

    public static ProblemDetailError InvalidRequest(string detail) =>
        AppErrors.Validation("enrollment.invalid_request", detail);

    public static ProblemDetailError NotFound(Guid enrollmentId) =>
        AppErrors.NotFound("enrollment.not_found", $"Enrollment '{enrollmentId}' was not found.");

    public static ProblemDetailError RequestIdTaken(Guid enrollmentId) =>
        AppErrors.Conflict("enrollment.request_id_taken", $"Request id '{enrollmentId}' is already used by another enrollment.");

    public static ProblemDetailError InvalidTransition(EnrollmentStatus from, EnrollmentStatus to) =>
        AppErrors.Conflict("enrollment.invalid_transition", $"An enrollment cannot go from {from} to {to}.");

    public static ProblemDetailError NoSeatReserved(Guid enrollmentId) =>
        AppErrors.Conflict("enrollment.no_seat", $"Enrollment '{enrollmentId}' has no reserved seat.");

    public static ProblemDetailError ConcurrencyConflict(Guid enrollmentId) =>
        AppErrors.Concurrency(ConcurrencyConflictCode, $"Enrollment '{enrollmentId}' was modified concurrently.");

    public static ProblemDetailError SagaUnavailable(Guid enrollmentId) =>
        AppErrors.Unavailable("enrollment.saga_unavailable", $"The enrollment workflow for '{enrollmentId}' could not be started; retry with the same request id.");
}
