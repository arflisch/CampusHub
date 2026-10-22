using Arc4u.Results;
using FluentResults;

namespace Dev4u.CampusHub.Errors;

/// <summary>
/// Catalogue of the expected failures shared by every service.
/// <para>
/// Each error is an Arc4u <see cref="ProblemDetailError"/> so that <c>ToProblemDetails()</c> turns it into the right
/// HTTP status without any mapping code in the APIs. The stable, machine-readable code is stored in the
/// <see cref="CodeKey"/> metadata (clients, tests and dashboards match on it, never on the message), and the
/// <see cref="TransientKey"/> metadata tells consumers (Dapr subscribers, Hangfire jobs, workflow activities)
/// whether retrying the same operation later can succeed.
/// </para>
/// </summary>
public static class AppErrors
{
    public const string CodeKey = "code";
    public const string TransientKey = "transient";

    public static ProblemDetailError Validation(string code, string detail) =>
        Create(code, detail, "Invalid request.", 400);

    public static ProblemDetailError NotFound(string code, string detail) =>
        Create(code, detail, "Resource not found.", 404);

    public static ProblemDetailError Forbidden(string code, string detail) =>
        Create(code, detail, "Operation not allowed.", 403);

    public static ProblemDetailError Conflict(string code, string detail) =>
        Create(code, detail, "Conflict with the current state.", 409);

    public static ProblemDetailError BusinessRule(string code, string detail) =>
        Create(code, detail, "Business rule violated.", 422);

    public static ProblemDetailError Concurrency(string code, string detail) =>
        Create(code, detail, "The resource was modified concurrently.", 409).WithMetadata(TransientKey, true);

    public static ProblemDetailError Unavailable(string code, string detail) =>
        Create(code, detail, "A dependency is unavailable.", 503).WithMetadata(TransientKey, true);

    /// <summary>True when at least one error says that retrying later can succeed.</summary>
    public static bool AreTransient(this IEnumerable<IError> errors) =>
        errors.Any(e => e.Metadata.TryGetValue(TransientKey, out var transient) && transient is true);

    /// <summary>The code of the first coded error, or "unknown".</summary>
    public static string FirstCode(this IEnumerable<IError> errors) =>
        errors.Select(e => e.Metadata.TryGetValue(CodeKey, out var code) ? code as string : null)
              .FirstOrDefault(code => code is not null) ?? "unknown";

    /// <summary>True when one of the errors carries the given code.</summary>
    public static bool HasCode(this IEnumerable<IError> errors, string code) =>
        errors.Any(e => e.Metadata.TryGetValue(CodeKey, out var value) && string.Equals(value as string, code, StringComparison.Ordinal));

    private static ProblemDetailError Create(string code, string detail, string title, int statusCode) =>
        ProblemDetailError.Create(detail)
                          .WithTitle(title)
                          .WithStatusCode(statusCode)
                          .WithMetadata(CodeKey, code);
}
