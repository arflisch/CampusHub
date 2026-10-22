using Dev4u.CampusHub.Catalog.Domain.Courses;
using Dev4u.CampusHub.Errors;
using FluentResults;

namespace Dev4u.CampusHub.Catalog.UseCases.Common;

/// <summary>
/// Re-runs a load → change → save cycle when the save lost an optimistic-concurrency race. Each attempt reloads
/// the aggregate, so the business rules are re-evaluated against the latest state (the last seat may be gone).
/// </summary>
internal static class ConcurrencyRetry
{
    public static async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> attempt, CancellationToken cancellationToken)
    {
        Result<T> result = Result.Fail<T>(AppErrors.Concurrency("concurrency.not_attempted", "No attempt was made."));
        for (var i = 0; i < Constants.ConcurrencyAttempts; i++)
        {
            result = await attempt(cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess || !result.Errors.HasCode(CourseErrors.ConcurrencyConflictCode))
            {
                return result;
            }
        }

        return result;
    }
}
