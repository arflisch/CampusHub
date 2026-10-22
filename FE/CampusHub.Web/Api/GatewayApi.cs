using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dev4u.CampusHub.Web.Presentation.Errors;
using FluentResults;

namespace Dev4u.CampusHub.Web.Api;

/// <summary>
/// HTTP plumbing shared by the gateway adapters: sends a request, reads the JSON body on success, and turns the
/// RFC 9457 problem details of the services (with their "code" extension) into <see cref="UiError"/>s. Network
/// failures and timeouts become a transient "api.unavailable" error instead of an exception.
/// </summary>
internal static class GatewayApi
{
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    public static async Task<Result<T>> SendAsync<TWire, T>(
        HttpClient http, Func<HttpRequestMessage> createRequest, Func<TWire, T> map, CancellationToken cancellationToken)
    {
        try
        {
            using var request = createRequest();
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Fail<T>(await ToErrorAsync(response, cancellationToken));
            }

            if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
            {
                // E.g. an HTML page: a proxy error page, or the SPA fallback when a route is missing.
                return Result.Fail<T>(UnexpectedResponse());
            }

            var wire = await response.Content.ReadFromJsonAsync<TWire>(Json, cancellationToken);
            return wire is null ? Result.Fail<T>(UnexpectedResponse()) : Result.Ok(map(wire));
        }
        catch (JsonException)
        {
            return Result.Fail<T>(UnexpectedResponse());
        }
        catch (HttpRequestException)
        {
            return Result.Fail<T>(UiError.Unavailable());
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Fail<T>(UiError.Unavailable()); // HttpClient timeout, not the caller's cancellation
        }
    }

    private static UiError UnexpectedResponse() =>
        new("api.unexpected_response", "The server sent an unexpected answer. Please try again later.", isTransient: true);

    public static HttpRequestMessage Get(string uri) => new(HttpMethod.Get, uri);

    public static HttpRequestMessage Post(string uri, object? body = null) =>
        new(HttpMethod.Post, uri) { Content = body is null ? null : JsonContent.Create(body, options: Json) };

    private static async Task<UiError> ToErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var status = (int)response.StatusCode;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new UiError("auth.session_expired", "Your session expired.");
        }

        ProblemResponse? problem = null;
        if (response.Content.Headers.ContentType?.MediaType is "application/problem+json" or "application/json")
        {
            try
            {
                problem = await response.Content.ReadFromJsonAsync<ProblemResponse>(Json, cancellationToken);
            }
            catch (JsonException)
            {
                // Not a problem document: fall back to the status code below.
            }
        }

        var transient = status >= 500 || status is 408 or 429;
        var validationMessage = problem?.Errors?.Values.SelectMany(v => v).FirstOrDefault();
        if (validationMessage is not null)
        {
            // Arc4u formats validation messages as "Severity: message".
            var separator = validationMessage.IndexOf(": ", StringComparison.Ordinal);
            return new UiError("validation", separator > 0 ? validationMessage[(separator + 2)..] : validationMessage);
        }

        var code = problem?.Code ?? $"http.{status}";
        var message = problem?.Detail ?? problem?.Title ?? response.ReasonPhrase ?? "The request failed.";
        return new UiError(code, message, transient);
    }

    private sealed record ProblemResponse(string? Title, string? Detail, int? Status, string? Code, Dictionary<string, string[]>? Errors);
}
