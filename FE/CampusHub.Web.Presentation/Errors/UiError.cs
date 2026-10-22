using FluentResults;

namespace Dev4u.CampusHub.Web.Presentation.Errors;

/// <summary>
/// A failure as seen by the front: the stable code sent by the services (ProblemDetails "code" extension), a message,
/// and whether retrying later can succeed.
/// </summary>
public sealed class UiError : Error
{
    public UiError(string code, string message, bool isTransient = false) : base(message)
    {
        Code = code;
        IsTransient = isTransient;
    }

    public string Code { get; }

    public bool IsTransient { get; }

    public static UiError Unavailable() =>
        new("api.unavailable", "The server can't be reached. Check your connection and try again.", isTransient: true);
}

public static class UiErrors
{
    public static bool AreTransient(this IEnumerable<IError> errors) =>
        errors.OfType<UiError>().Any(e => e.IsTransient);

    /// <summary>A message a user can act on: a friendly text for the known codes, the server message otherwise.</summary>
    public static string Describe(IEnumerable<IError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        var first = errors.FirstOrDefault();
        return first switch
        {
            UiError error => ForCode(error.Code) ?? error.Message,
            null => "Something went wrong.",
            _ => first.Message,
        };
    }

    /// <summary>Friendly text for the error codes of the CampusHub services (also used for rejection reasons).</summary>
    public static string? ForCode(string? code) => code switch
    {
        "course.full" => "This course is full.",
        "course.not_open" => "This course is not open for enrollment.",
        "course.started" => "This course has already started: enrollments are closed.",
        "course.invalid_schedule" => "Check the dates: a course starts in the future and lasts at most a year.",
        "course.already_enrolled" => "You already hold a seat in this course.",
        "course.code_taken" => "A course with this code already exists.",
        "course.not_owner" => "Only the teacher of this course can change it.",
        "course.not_found" => "This course no longer exists.",
        "reservation.expired" => "Your seat was held for too long and was given back.",
        "catalog.unavailable" => "The catalogue was unavailable. Please try again.",
        "enrollment.confirmation_failed" => "Your enrollment could not be confirmed. The seat was given back.",
        "enrollment.saga_unavailable" => "Your request could not be processed right now. Please try again.",
        "api.unavailable" => "The server can't be reached. Check your connection and try again.",
        "auth.session_expired" => "Your session expired. Reloading…",
        "http.403" => "You are not allowed to do this.",
        _ => null,
    };
}
