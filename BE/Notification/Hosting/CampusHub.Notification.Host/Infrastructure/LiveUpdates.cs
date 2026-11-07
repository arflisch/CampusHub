using System.Security.Claims;
using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Notification.Domain.Notifications;
using Dev4u.CampusHub.Rights;
using Dev4u.CampusHub.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Dev4u.CampusHub.Notification.Host.Infrastructure;

/// <summary>
/// SignalR hub the browsers connect to through the gateway (/notification/hubs/campus), which forwards the session's
/// access token on the WebSocket upgrade. The hub only pushes; clients never send anything to it.
/// </summary>
[Authorize(Policy = nameof(Access.AccessApplication))]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "SignalR maps public hubs.")]
public sealed class LiveHub : Hub
{
    /// <summary>Client method: one of the user's enrollments changed status.</summary>
    public const string EnrollmentChanged = "enrollmentChanged";

    /// <summary>Client method: the user received a notification.</summary>
    public const string NotificationCreated = "notificationCreated";
}

/// <summary>Messages are addressed to a user by the token's subject, like every other per-user data of CampusHub.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Registered in Program.cs.")]
internal sealed class SubjectUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => connection?.User is { } user ? user.GetUserId() : null;
}

/// <summary>Pushes to every open browser of a user. Best effort: a failure is logged, never returned.</summary>
[Export(typeof(ILiveUpdates)), Scoped]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the Arc4u dependency injection ([Export]).")]
internal sealed partial class SignalRLiveUpdates(IHubContext<LiveHub> hub, ILogger<SignalRLiveUpdates> logger) : ILiveUpdates
{
    public Task NotificationCreatedAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return SendAsync(notification.RecipientId, LiveHub.NotificationCreated,
                         new { notification.Id, notification.Title, notification.Message, notification.CreatedAt }, cancellationToken);
    }

    public Task EnrollmentChangedAsync(string studentId, Guid enrollmentId, Guid courseId, string status, CancellationToken cancellationToken) =>
        SendAsync(studentId, LiveHub.EnrollmentChanged, new { enrollmentId, courseId, status }, cancellationToken);

    private async Task SendAsync(string userId, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients.User(userId).SendAsync(method, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPushFailed(logger, ex, method);
        }
    }

    [LoggerMessage(EventId = 4500, Level = LogLevel.Warning, Message = "Live push {Method} failed; the change will be seen at the next load.")]
    private static partial void LogPushFailed(ILogger logger, Exception exception, string method);
}
