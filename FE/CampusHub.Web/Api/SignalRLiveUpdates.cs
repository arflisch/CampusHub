using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Services;
using Microsoft.AspNetCore.SignalR.Client;

namespace Dev4u.CampusHub.Web.Api;

/// <summary>
/// One SignalR connection to the Notification hub, through the gateway: same origin, so the browser sends the
/// session cookie and the gateway attaches the access token on the WebSocket upgrade. It reconnects on its own;
/// if it cannot connect at all, the screens keep working from their loads and polling.
/// </summary>
internal sealed class SignalRLiveUpdates : ILiveUpdates, IAsyncDisposable
{
    private readonly HubConnection _connection;
    private readonly SemaphoreSlim _starting = new(1, 1);

    public SignalRLiveUpdates(Uri hubUrl)
    {
        _connection = new HubConnectionBuilder().WithUrl(hubUrl).WithAutomaticReconnect().Build();

        // Names and payloads mirror Notification's LiveHub / SignalRLiveUpdates.
        _connection.On<EnrollmentChangedMessage>("enrollmentChanged", message =>
        {
            if (Enum.TryParse<EnrollmentState>(message.Status, ignoreCase: true, out var state))
            {
                EnrollmentChanged?.Invoke(this, new EnrollmentChange(message.EnrollmentId, message.CourseId, state));
            }
        });
        _connection.On<NotificationCreatedMessage>("notificationCreated", message =>
            NotificationReceived?.Invoke(this, new NotificationItem(message.Id, message.Title, message.Message, message.CreatedAt, IsRead: false)));
    }

    public event EventHandler<EnrollmentChange>? EnrollmentChanged;

    public event EventHandler<NotificationItem>? NotificationReceived;

    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _starting.WaitAsync(cancellationToken);
        try
        {
            if (_connection.State == HubConnectionState.Disconnected)
            {
                await _connection.StartAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TimeoutException)
        {
            // Not connected: the pages still load and poll. The next StartAsync (next shell load) tries again.
        }
        finally
        {
            _starting.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        _starting.Dispose();
    }

    private sealed record EnrollmentChangedMessage(Guid EnrollmentId, Guid CourseId, string Status);

    private sealed record NotificationCreatedMessage(Guid Id, string Title, string Message, DateTimeOffset CreatedAt);
}
