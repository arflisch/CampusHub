using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Errors;
using Dev4u.CampusHub.Notification.Domain.Notifications;
using FluentResults;
using Dev4u.CampusHub.Notification.UseCases.Common;

namespace Dev4u.CampusHub.Notification.UseCases.Notifications;

public sealed record NotificationDto(Guid Id, string Title, string Message, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record NotifyEnrollmentConfirmedCommand(Guid EventId, string StudentId, string CourseTitle, DateTimeOffset OccurredAt);

/// <summary>
/// Reacts to EnrollmentConfirmed: the student gets an in-app notification. Idempotent (inbox through the event
/// id), so the at-least-once delivery of Dapr never creates duplicates.
/// </summary>
[Export, Scoped]
public sealed class NotifyEnrollmentConfirmed(INotificationRepository notifications, ILiveUpdates live)
{
    public async Task<Result<bool>> ExecuteAsync(NotifyEnrollmentConfirmedCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Event payloads are input like any other: an unusable event is dropped, retrying would not fix it.
        if (command.EventId == Guid.Empty || string.IsNullOrWhiteSpace(command.StudentId) || string.IsNullOrWhiteSpace(command.CourseTitle))
        {
            return Result.Fail<bool>(AppErrors.Validation("notification.invalid_event", "The EnrollmentConfirmed event is incomplete."));
        }

        var notification = UserNotification.EnrollmentConfirmed(command.EventId, command.StudentId, command.CourseTitle, command.OccurredAt);
        return await notifications.StoreAndPushAsync(live, notification, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record NotifyClassStartsSoonCommand(Guid EventId, string StudentId, string CourseTitle, DateTimeOffset CourseStartsAt, DateTimeOffset OccurredAt);

/// <summary>Reacts to ClassReminderDue: the student is reminded that the course starts soon. Idempotent like the others.</summary>
[Export, Scoped]
public sealed class NotifyClassStartsSoon(INotificationRepository notifications, ILiveUpdates live)
{
    public async Task<Result<bool>> ExecuteAsync(NotifyClassStartsSoonCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.EventId == Guid.Empty || string.IsNullOrWhiteSpace(command.StudentId) || string.IsNullOrWhiteSpace(command.CourseTitle))
        {
            return Result.Fail<bool>(AppErrors.Validation("notification.invalid_event", "The ClassReminderDue event is incomplete."));
        }

        var notification = UserNotification.ClassStartsSoon(command.EventId, command.StudentId, command.CourseTitle, command.CourseStartsAt,
                                                            command.OccurredAt);
        return await notifications.StoreAndPushAsync(live, notification, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record NotifyCertificateIssuedCommand(Guid EventId, string StudentId, string CourseTitle, string CertificateNumber, DateTimeOffset OccurredAt);

/// <summary>Reacts to CertificateIssued (Grading): the student learns that the certificate is available. Idempotent.</summary>
[Export, Scoped]
public sealed class NotifyCertificateIssued(INotificationRepository notifications, ILiveUpdates live)
{
    public async Task<Result<bool>> ExecuteAsync(NotifyCertificateIssuedCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.EventId == Guid.Empty || string.IsNullOrWhiteSpace(command.StudentId)
            || string.IsNullOrWhiteSpace(command.CourseTitle) || string.IsNullOrWhiteSpace(command.CertificateNumber))
        {
            return Result.Fail<bool>(AppErrors.Validation("notification.invalid_event", "The CertificateIssued event is incomplete."));
        }

        var notification = UserNotification.CertificateIssued(command.EventId, command.StudentId, command.CourseTitle, command.CertificateNumber,
                                                              command.OccurredAt);
        return await notifications.StoreAndPushAsync(live, notification, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record NotifyEnrollmentCancelledCommand(Guid EventId, string StudentId, string CourseTitle, string Reason, int RefundedInCents,
                                                       DateTimeOffset OccurredAt);

/// <summary>Reacts to EnrollmentCancelled: the student learns that the enrollment is cancelled and refunded. Idempotent.</summary>
[Export, Scoped]
public sealed class NotifyEnrollmentCancelled(INotificationRepository notifications, ILiveUpdates live)
{
    public async Task<Result<bool>> ExecuteAsync(NotifyEnrollmentCancelledCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.EventId == Guid.Empty || string.IsNullOrWhiteSpace(command.StudentId) || string.IsNullOrWhiteSpace(command.CourseTitle))
        {
            return Result.Fail<bool>(AppErrors.Validation("notification.invalid_event", "The EnrollmentCancelled event is incomplete."));
        }

        var notification = UserNotification.EnrollmentCancelled(command.EventId, command.StudentId, command.CourseTitle, command.Reason,
                                                                command.RefundedInCents, command.OccurredAt);
        return await notifications.StoreAndPushAsync(live, notification, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record RelayEnrollmentStatusCommand(string StudentId, Guid EnrollmentId, Guid CourseId, string Status);

/// <summary>
/// Reacts to EnrollmentStatusChanged: the student's open screens are told at once (SignalR), so the saga is followed
/// live instead of polled. Nothing is stored: a missed push only means the change is seen at the next load.
/// </summary>
[Export, Scoped]
public sealed class RelayEnrollmentStatus(ILiveUpdates live)
{
    public async Task<Result> ExecuteAsync(RelayEnrollmentStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.StudentId) || command.EnrollmentId == Guid.Empty)
        {
            return Result.Fail(AppErrors.Validation("notification.invalid_event", "The EnrollmentStatusChanged event is incomplete."));
        }

        await live.EnrollmentChangedAsync(command.StudentId, command.EnrollmentId, command.CourseId, command.Status, cancellationToken).ConfigureAwait(false);
        return Result.Ok();
    }
}

/// <summary>Marks the current user's notifications as read: one, or all of them.</summary>
[Export, Scoped]
public sealed class MarkMyNotificationsRead(INotificationRepository notifications, TimeProvider timeProvider)
{
    public async Task<Result<long>> ExecuteAsync(string recipientId, Guid? notificationId, CancellationToken cancellationToken) =>
        Result.Ok(await notifications.MarkReadAsync(recipientId, notificationId, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false));
}

internal static class NotificationStorage
{
    /// <summary>Stores the notification (inbox: once per event) and pushes it live when it is new.</summary>
    public static async Task<Result<bool>> StoreAndPushAsync(this INotificationRepository notifications, ILiveUpdates live,
                                                             UserNotification notification, CancellationToken cancellationToken)
    {
        var created = await notifications.AddIfAbsentAsync(notification, cancellationToken).ConfigureAwait(false);
        if (created)
        {
            NotificationMetrics.RecordCreated(notification.Title);
            await live.NotificationCreatedAsync(notification, cancellationToken).ConfigureAwait(false);
        }

        return Result.Ok(created);
    }
}

/// <summary>The current user's latest notifications.</summary>
[Export, Scoped]
public sealed class ListMyNotifications(INotificationRepository notifications)
{
    public const int MaxCount = 50;

    public async Task<Result<IReadOnlyList<NotificationDto>>> ExecuteAsync(string userId, CancellationToken cancellationToken)
    {
        var items = await notifications.ListForRecipientAsync(userId, MaxCount, cancellationToken).ConfigureAwait(false);
        return Result.Ok<IReadOnlyList<NotificationDto>>([.. items.Select(n => new NotificationDto(n.Id, n.Title, n.Message, n.CreatedAt, n.ReadAt))]);
    }
}
