using System.Net;
using Arc4u.Dependency.Attribute;
using Arc4u.Results;
using Dev4u.CampusHub.Billing.ServiceApi.Sdk;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Errors;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Billing;

/// <summary>
/// Anti-corruption layer over the Billing service API: calls the NSwag-generated client through the "Billing" http
/// client declared by the host (gateway + service-account token), and turns its exceptions into results. Billing's
/// error codes are kept (payment.declined is a business refusal); network failures, timeouts, 5xx, 429 and
/// concurrency conflicts are marked transient so the saga's retry policy handles them.
/// </summary>
[Export(typeof(ICoursePayments)), Scoped]
public sealed class BillingCoursePayments(IHttpClientFactory httpClientFactory, ILogger<BillingCoursePayments> logger) : ICoursePayments
{
    public const string HttpClientName = "Billing";
    private const string ApiVersion = "1.0";

    private PaymentsClient Client => new(httpClientFactory.CreateClient(HttpClientName));

    public async Task<Result> ChargeAsync(Guid enrollmentId, string studentId, Guid courseId, int amountInCents, CancellationToken cancellationToken)
    {
        try
        {
            await Client.ChargeAsync(ApiVersion, api_version: null, new ChargeEnrollmentCommand
            {
                EnrollmentId = enrollmentId,
                StudentId = studentId,
                CourseId = courseId,
                AmountInCents = amountInCents,
            }, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (TryTranslate(ex, cancellationToken, out var error))
        {
            logger.LogBillingCallFailed(ex, "charge", error.Message);
            return Result.Fail(error);
        }
    }

    public async Task<Result> RefundAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        try
        {
            await Client.RefundAsync(enrollmentId, ApiVersion, api_version: null, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (TryTranslate(ex, cancellationToken, out var error))
        {
            logger.LogBillingCallFailed(ex, "refund", error.Message);
            return Result.Fail(error);
        }
    }

    private static bool TryTranslate(Exception exception, CancellationToken cancellationToken, out IError error)
    {
        switch (exception)
        {
            case RestException<ProblemDetails> problem:
                error = FromStatus(problem.StatusCode, CodeOf(problem.Result), problem.Result.Detail ?? problem.Result.Title);
                return true;
            case RestException rest:
                error = FromStatus(rest.StatusCode, code: null, detail: null);
                return true;
            case HttpRequestException:
                error = AppErrors.Unavailable("billing.unavailable", "The Billing service could not be reached.");
                return true;
            case TaskCanceledException when !cancellationToken.IsCancellationRequested:
                error = AppErrors.Unavailable("billing.timeout", "The Billing service did not answer in time.");
                return true;
            default:
                error = null!;
                return false; // bugs and caller cancellation keep bubbling
        }
    }

    private static string? CodeOf(ProblemDetails problem) =>
        problem.AdditionalProperties.TryGetValue(AppErrors.CodeKey, out var code) ? code?.ToString() : null;

    private static ProblemDetailError FromStatus(int status, string? code, string? detail)
    {
        var errorCode = code ?? $"billing.http_{status}";
        var message = detail ?? $"Billing answered {status}.";
        return status switch
        {
            (int)HttpStatusCode.NotFound => AppErrors.NotFound(errorCode, message),
            (int)HttpStatusCode.Forbidden => AppErrors.Forbidden(errorCode, message),
            (int)HttpStatusCode.BadRequest => AppErrors.Validation(errorCode, message),
            (int)HttpStatusCode.UnprocessableEntity => AppErrors.BusinessRule(errorCode, message),
            (int)HttpStatusCode.Conflict when errorCode.EndsWith("concurrency_conflict", StringComparison.Ordinal) => AppErrors.Concurrency(errorCode, message),
            (int)HttpStatusCode.Conflict => AppErrors.Conflict(errorCode, message),
            // 401 (expired service token), 408, 429 and 5xx: worth retrying.
            _ => AppErrors.Unavailable(errorCode, message),
        };
    }
}
