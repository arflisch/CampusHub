using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Mvvm;
using Dev4u.CampusHub.Web.Presentation.Services;

namespace Dev4u.CampusHub.Web.Presentation.ViewModels;

/// <summary>State of the application shell: who is signed in.</summary>
public sealed partial class ShellViewModel(ICurrentUserService currentUser) : ViewModelBase
{
    [ObservableProperty]
    public partial CurrentUser? User { get; private set; }

    public override Task InitializeAsync(CancellationToken cancellationToken) => LoadCommand.ExecuteAsync(null);

    public override void Deactivate() => LoadCommand.Cancel();

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var result = await currentUser.GetAsync(cancellationToken);
        if (result.IsSuccess)
        {
            User = result.Value;
        }
        else
        {
            ShowErrors(result.Errors);
        }
    }
}
