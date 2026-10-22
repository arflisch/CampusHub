using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev4u.CampusHub.Web.Presentation.Errors;
using FluentResults;

namespace Dev4u.CampusHub.Web.Presentation.Mvvm;

/// <summary>
/// Base of every ViewModel: an error message for the view, and a lifecycle driven by the component that owns the
/// ViewModel (initialize when the page appears, deactivate when it goes away to stop background work).
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    public partial string? ErrorMessage { get; protected set; }

    /// <summary>Called once when the page is initialized.</summary>
    public virtual Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Called when the page is disposed: cancel running commands, stop timers.</summary>
    public virtual void Deactivate()
    {
    }

    protected void ShowErrors(IEnumerable<IError> errors) => ErrorMessage = UiErrors.Describe(errors);

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;
}
