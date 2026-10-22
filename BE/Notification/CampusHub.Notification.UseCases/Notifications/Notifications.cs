using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Errors;
using Dev4u.CampusHub.Notification.Domain.Notifications;
using FluentResults;

namespace Dev4u.CampusHub.Notification.UseCases.Notifications;

public sealed record NotificationDto(Guid Id, string Title, string Message, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record NotifyEnrollmentConfirmedCommand(Guid EventId, string StudentId, string CourseTitle, DateTimeOffset OccurredAt);

/// <summary>
/// Reacts to EnrollmentConfirmed: the student gets an in-app notification. Idempotent (inbox through the event
/// id), so the at-least-once delivery of Dapr never creates duplicates.
/// </summary>
[Export, Scoped]
public sealed class NotifyEnrollmentConfirmed(INotificationRepository notifications)
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
        var created = await notifications.AddIfAbsentAsync(notification, cancellationToken).ConfigureAwait(false);
        return Result.Ok(created);
    }
}

public sealed record NotifyClassStartsSoonCommand(Guid EventId, string StudentId, string CourseTitle, DateTimeOffset CourseStartsAt, DateTimeOffset OccurredAt);

/// <summary>Reacts to ClassReminderDue: the student is reminded that the course starts soon. Idempotent like the others.</summary>
[Export, Scoped]
public sealed class NotifyClassStartsSoon(INotificationRepository notifications)
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
        return Result.Ok(await notifications.AddIfAbsentAsync(notification, cancellationToken).ConfigureAwait(false));
    }
}

public sealed record NotifyCertificateIssuedCommand(Guid EventId, string StudentId, string CourseTitle, string CertificateNumber, DateTimeOffset OccurredAt);

/// <summary>Reacts to CertificateIssued (Grading): the student learns that the certificate is available. Idempotent.</summary>
[Export, Scoped]
public sealed class NotifyCertificateIssued(INotificationRepository notifications)
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
        return Result.Ok(await notifications.AddIfAbsentAsync(notification, cancellationToken).ConfigureAwait(false));
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
