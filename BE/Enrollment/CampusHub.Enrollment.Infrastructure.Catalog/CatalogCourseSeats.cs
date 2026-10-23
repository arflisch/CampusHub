using System.Net;
using Arc4u.Dependency.Attribute;
using Arc4u.Results;
using Dev4u.CampusHub.Catalog.ServiceApi.Sdk;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Errors;
using FluentResults;
using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Catalog;

/// <summary>
/// Anti-corruption layer over the Catalog service API: calls the NSwag-generated client through the "Catalog"
/// http client declared by the host (gateway + service-account token), and turns its exceptions into results.
/// Catalog's error codes are kept; network failures, timeouts, 5xx, 429 and concurrency conflicts are marked
/// transient so the saga's retry policy handles them.
/// </summary>
[Export(typeof(ICourseSeats)), Scoped]
public sealed class CatalogCourseSeats(IHttpClientFactory httpClientFactory, ILogger<CatalogCourseSeats> logger) : ICourseSeats
{
    public const string HttpClientName = "Catalog";
    private const string ApiVersion = "1.0";

    private SeatReservationsClient Client => new(httpClientFactory.CreateClient(HttpClientName));

    public async Task<Result<SeatHold>> ReserveAsync(Guid courseId, string studentId, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            var reservation = await Client.ReserveAsync(courseId, ApiVersion, api_version: null,
                new ReserveSeatCommand { StudentId = studentId, IdempotencyKey = idempotencyKey }, cancellationToken).ConfigureAwait(false);
            return Result.Ok(new SeatHold(reservation.ReservationId, reservation.CourseTitle,
                                          AsUtc(reservation.CourseStartsAt), AsUtc(reservation.CourseEndsAt), reservation.CoursePriceInCents));
        }
        catch (Exception ex) when (TryTranslate(ex, cancellationToken, out var error))
        {
            logger.LogCatalogCallFailed(ex, "reserve", error.Message);
            return Result.Fail<SeatHold>(error);
        }
    }

    public async Task<Result> ConfirmAsync(Guid courseId, Guid reservationId, CancellationToken cancellationToken)
    {
        try
        {
            await Client.ConfirmAsync(courseId, reservationId, ApiVersion, api_version: null, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (TryTranslate(ex, cancellationToken, out var error))
        {
            logger.LogCatalogCallFailed(ex, "confirm", error.Message);
            return Result.Fail(error);
        }
    }

    public async Task<Result> ReleaseAsync(Guid courseId, Guid reservationId, CancellationToken cancellationToken)
    {
        try
        {
            await Client.ReleaseAsync(courseId, reservationId, ApiVersion, api_version: null, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (TryTranslate(ex, cancellationToken, out var error))
        {
            logger.LogCatalogCallFailed(ex, "release", error.Message);
            return Result.Fail(error);
        }
    }

    // The generated client exposes dates as DateTime; the service sends them in UTC.
    private static DateTimeOffset AsUtc(DateTime value) =>
        new(value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime());

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
                error = AppErrors.Unavailable("catalog.unavailable", "The Catalog service could not be reached.");
                return true;
            case TaskCanceledException when !cancellationToken.IsCancellationRequested:
                error = AppErrors.Unavailable("catalog.timeout", "The Catalog service did not answer in time.");
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
        var errorCode = code ?? $"catalog.http_{status}";
        var message = detail ?? $"Catalog answered {status}.";
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
