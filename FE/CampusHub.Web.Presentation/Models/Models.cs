namespace Dev4u.CampusHub.Web.Presentation.Models;

public enum CourseState
{
    Draft,
    Published,
    Closed,
}

/// <summary>A course as shown in the catalogue and in the teacher's list.</summary>
public sealed record CourseItem(
    Guid Id,
    string Code,
    string Title,
    string TeacherId,
    int Capacity,
    int SeatsLeft,
    int PriceInCents,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    CourseState State,
    DateTimeOffset? PublishedAt)
{
    public bool IsFull => SeatsLeft == 0;

    public bool IsFree => PriceInCents == 0;
}

/// <summary>Display of amounts, all in EUR.</summary>
public static class Money
{
    public static string Format(int cents) =>
        cents == 0 ? "Free" : (cents / 100m).ToString("0.00 €", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Where the signed-in student stands with a course of the catalogue.</summary>
public enum CourseEnrollment
{
    /// <summary>Not enrolled (or the last request was rejected): the course can be requested.</summary>
    None,

    /// <summary>A request is being processed by the saga.</summary>
    InProgress,

    /// <summary>The enrollment is confirmed.</summary>
    Enrolled,
}

/// <summary>Progress of an enrollment saga, as reported by the Enrollment service.</summary>
public enum EnrollmentState
{
    Requested,
    SeatHeld,
    Confirmed,
    Rejected,

    /// <summary>The seat is held and the course paid (paid courses only).</summary>
    Paid,
}

public sealed record EnrollmentItem(
    Guid Id,
    Guid CourseId,
    string? CourseTitle,
    DateTimeOffset? CourseStartsAt,
    int CoursePriceInCents,
    EnrollmentState State,
    string? RejectionCode,
    DateTimeOffset RequestedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Confirmed or rejected: the saga is over and the item no longer needs polling.</summary>
    public bool IsSettled => State is EnrollmentState.Confirmed or EnrollmentState.Rejected;
}

public sealed record NotificationItem(Guid Id, string Title, string Message, DateTimeOffset CreatedAt, bool IsRead);

/// <summary>The signed-in user and what the gateway says they may do (the services check the rights again).</summary>
public sealed record CurrentUser(string UserId, string DisplayName, bool IsTeacher = false, bool IsAdmin = false);
