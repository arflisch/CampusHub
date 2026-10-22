using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Errors;
using FluentResults;

namespace Dev4u.CampusHub.Enrollment.UseCases.Common;

/// <summary>Re-runs a load → change → save cycle when the save lost an optimistic-concurrency race.</summary>
internal static class ConcurrencyRetry
{
    public static async Task<Result> ExecuteAsync(Func<CancellationToken, Task<Result>> attempt, CancellationToken cancellationToken)
    {
        var result = Result.Fail(EnrollmentErrors.ConcurrencyConflict(Guid.Empty));
        for (var i = 0; i < Constants.ConcurrencyAttempts; i++)
        {
            result = await attempt(cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess || !result.Errors.HasCode(EnrollmentErrors.ConcurrencyConflictCode))
            {
                return result;
            }
        }

        return result;
    }
}
