using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Mvvm;
using Dev4u.CampusHub.Web.Presentation.Services;

namespace Dev4u.CampusHub.Web.Presentation.ViewModels;

/// <summary>
/// State of the application shell: who is signed in, which menus they get, and how many notifications they have not
/// read. The shell also opens the live connection (SignalR) the other screens listen to.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly ICurrentUserService _currentUser;
    private readonly INotificationService _notifications;
    private readonly ILiveUpdates _live;
    private readonly NotificationCounter _counter;

    public ShellViewModel(ICurrentUserService currentUser, INotificationService notifications, ILiveUpdates live, NotificationCounter counter)
    {
        _currentUser = currentUser;
        _notifications = notifications;
        _live = live;
        _counter = counter;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTeacher))]
    public partial CurrentUser? User { get; private set; }

    public bool IsTeacher => User?.IsTeacher == true;

    public int UnreadCount => _counter.Unread;

    public override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _counter.PropertyChanged += OnCounterChanged;
        _live.NotificationReceived += OnNotificationReceived;
        await LoadCommand.ExecuteAsync(null);
        await _live.StartAsync(cancellationToken); // best effort: without it the screens still refresh on load
    }

    public override void Deactivate()
    {
        LoadCommand.Cancel();
        _counter.PropertyChanged -= OnCounterChanged;
        _live.NotificationReceived -= OnNotificationReceived;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var user = await _currentUser.GetAsync(cancellationToken);
        if (user.IsFailed)
        {
            ShowErrors(user.Errors);
            return;
        }

        User = user.Value;
        var notifications = await _notifications.ListMineAsync(cancellationToken);
        if (notifications.IsSuccess)
        {
            _counter.Unread = notifications.Value.Count(n => !n.IsRead);
        }
    }

    private void OnNotificationReceived(object? sender, NotificationItem notification) => _counter.Unread++;

    private void OnCounterChanged(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(UnreadCount));
}
