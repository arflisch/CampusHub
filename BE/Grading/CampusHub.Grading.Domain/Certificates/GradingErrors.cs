using Arc4u.Results;
using Dev4u.CampusHub.Errors;

namespace Dev4u.CampusHub.Grading.Domain.Certificates;

/// <summary>Every failure the Grading domain can return, with its stable code.</summary>
public static class GradingErrors
{
    public const string ConcurrencyConflictCode = "participation.concurrency_conflict";

    public static ProblemDetailError InvalidParticipation(string detail) =>
        AppErrors.Validation("participation.invalid", detail);

    public static ProblemDetailError CourseNotEnded(Guid enrollmentId) =>
        AppErrors.BusinessRule("certificate.course_not_ended", $"The course of enrollment '{enrollmentId}' has not ended yet.");

    public static ProblemDetailError ConcurrencyConflict(Guid enrollmentId) =>
        AppErrors.Concurrency(ConcurrencyConflictCode, $"Participation '{enrollmentId}' was modified concurrently.");
}
