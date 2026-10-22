namespace Dev4u.CampusHub.Notification.Domain.Notifications;

/// <summary>
/// A message shown to a user in the application. Its id is the id of the integration event that caused it, so
/// a redelivered event (Dapr delivers at least once) maps to the same notification and is stored only once.
/// </summary>
public sealed class UserNotification
{
    private UserNotification(Guid id, string recipientId, string title, string message, DateTimeOffset createdAt, DateTimeOffset? readAt)
    {
        Id = id;
        RecipientId = recipientId;
        Title = title;
        Message = message;
        CreatedAt = createdAt;
        ReadAt = readAt;
    }

    public Guid Id { get; }

    /// <summary>Identity provider subject of the recipient.</summary>
    public string RecipientId { get; }

    public string Title { get; }

    public string Message { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? ReadAt { get; private set; }

    public static UserNotification EnrollmentConfirmed(Guid sourceEventId, string studentId, string courseTitle, DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(courseTitle);

        return new UserNotification(sourceEventId, studentId, "Enrollment confirmed",
                                    $"Your enrollment in \"{courseTitle}\" is confirmed.", occurredAt, readAt: null);
    }

    public static UserNotification ClassStartsSoon(Guid sourceEventId, string studentId, string courseTitle, DateTimeOffset startsAt,
                                                   DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(courseTitle);

        var when = startsAt.UtcDateTime.ToString("dddd d MMMM yyyy 'at' HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        return new UserNotification(sourceEventId, studentId, "Your course starts soon",
                                    $"\"{courseTitle}\" starts on {when}.", occurredAt, readAt: null);
    }

    public static UserNotification CertificateIssued(Guid sourceEventId, string studentId, string courseTitle, string certificateNumber,
                                                     DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(courseTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateNumber);

        return new UserNotification(sourceEventId, studentId, "Certificate issued",
                                    $"You completed \"{courseTitle}\": certificate {certificateNumber}.", occurredAt, readAt: null);
    }

    /// <summary>Rebuilds a notification from storage. Persistence only.</summary>
    public static UserNotification Rehydrate(Guid id, string recipientId, string title, string message, DateTimeOffset createdAt, DateTimeOffset? readAt) =>
        new(id, recipientId, title, message, createdAt, readAt);
}
