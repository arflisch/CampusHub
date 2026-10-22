namespace Dev4u.CampusHub.Notification.Domain.Notifications;

/// <summary>Port implemented by the MongoDB adapter.</summary>
public interface INotificationRepository
{
    /// <summary>Stores the notification unless one with the same id exists. Returns false for a duplicate.</summary>
    Task<bool> AddIfAbsentAsync(UserNotification notification, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserNotification>> ListForRecipientAsync(string recipientId, int take, CancellationToken cancellationToken);
}
