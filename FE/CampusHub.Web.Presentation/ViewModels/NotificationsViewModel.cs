using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Mvvm;
using Dev4u.CampusHub.Web.Presentation.Services;

namespace Dev4u.CampusHub.Web.Presentation.ViewModels;

/// <summary>The current user's notifications (written by the Notification service when an event arrives).</summary>
public sealed partial class NotificationsViewModel : ViewModelBase
{
    private readonly INotificationService _notifications;

    public NotificationsViewModel(INotificationService notifications)
    {
        _notifications = notifications;
        Notifications = [];
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<NotificationItem> Notifications { get; private set; }

    public bool IsEmpty => Notifications.Count == 0;

    public override Task InitializeAsync(CancellationToken cancellationToken) => LoadCommand.ExecuteAsync(null);

    public override void Deactivate() => LoadCommand.Cancel();

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var result = await _notifications.ListMineAsync(cancellationToken);
        if (result.IsSuccess)
        {
            Notifications = result.Value;
        }
        else
        {
            ShowErrors(result.Errors);
        }
    }
}
