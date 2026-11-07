using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Mvvm;
using Dev4u.CampusHub.Web.Presentation.Services;

namespace Dev4u.CampusHub.Web.Presentation.ViewModels;

/// <summary>
/// The current user's notifications (written by the Notification service when an event arrives). New ones appear
/// at once through the live connection; they can be marked as read.
/// </summary>
public sealed partial class NotificationsViewModel : ViewModelBase
{
    private readonly INotificationService _notifications;
    private readonly ILiveUpdates _live;
    private readonly NotificationCounter _counter;

    public NotificationsViewModel(INotificationService notifications, ILiveUpdates live, NotificationCounter counter)
    {
        _notifications = notifications;
        _live = live;
        _counter = counter;
        Notifications = [];
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(MarkAllReadCommand))]
    public partial IReadOnlyList<NotificationItem> Notifications { get; private set; }

    public bool IsEmpty => Notifications.Count == 0;

    public override Task InitializeAsync(CancellationToken cancellationToken)
    {
        _live.NotificationReceived += OnNotificationReceived;
        return LoadCommand.ExecuteAsync(null);
    }

    public override void Deactivate()
    {
        LoadCommand.Cancel();
        _live.NotificationReceived -= OnNotificationReceived;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var result = await _notifications.ListMineAsync(cancellationToken);
        if (result.IsSuccess)
        {
            Notifications = result.Value;
            _counter.Unread = result.Value.Count(n => !n.IsRead);
        }
        else
        {
            ShowErrors(result.Errors);
        }
    }

    private bool HasUnread() => Notifications.Any(n => !n.IsRead);

    [RelayCommand(CanExecute = nameof(HasUnread))]
    private async Task MarkAllReadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var result = await _notifications.MarkAllReadAsync(cancellationToken);
        if (result.IsFailed)
        {
            ShowErrors(result.Errors);
            return;
        }

        Notifications = [.. Notifications.Select(n => n with { IsRead = true })];
        _counter.Unread = 0;
    }

    // The shell counts it; this page only shows it.
    private void OnNotificationReceived(object? sender, NotificationItem notification)
    {
        if (Notifications.All(n => n.Id != notification.Id))
        {
            Notifications = [notification, .. Notifications];
        }
    }
}
