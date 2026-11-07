namespace Dev4u.CampusHub.Notification.Domain.Notifications;

/// <summary>Port implemented by the MongoDB adapter.</summary>
public interface INotificationRepository
{
    /// <summary>Stores the notification unless one with the same id exists. Returns false for a duplicate.</summary>
    Task<bool> AddIfAbsentAsync(UserNotification notification, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserNotification>> ListForRecipientAsync(string recipientId, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the recipient's unread notifications as read: one (<paramref name="notificationId"/>) or all of them
    /// (null). Only the recipient's own notifications match. Returns how many were marked.
    /// </summary>
    Task<long> MarkReadAsync(string recipientId, Guid? notificationId, DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// Port pushing changes to the browsers of a user (SignalR). Best effort: a user who is not connected simply misses
/// the push and sees the change at the next load, so a failure is never an error for the caller.
/// </summary>
public interface ILiveUpdates
{
    Task NotificationCreatedAsync(UserNotification notification, CancellationToken cancellationToken);

    Task EnrollmentChangedAsync(string studentId, Guid enrollmentId, Guid courseId, string status, CancellationToken cancellationToken);
}
