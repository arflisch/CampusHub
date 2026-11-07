using CommunityToolkit.Mvvm.ComponentModel;

namespace Dev4u.CampusHub.Web.Presentation.Services;

/// <summary>
/// Number of unread notifications, shared by the shell (badge in the menu) and the notifications page (which marks
/// them read). One instance for the whole application.
/// </summary>
public sealed partial class NotificationCounter : ObservableObject
{
    [ObservableProperty]
    public partial int Unread { get; set; }
}
