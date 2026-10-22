using System.ComponentModel;
using Dev4u.CampusHub.Web.Presentation.Mvvm;
using Microsoft.AspNetCore.Components;

namespace Dev4u.CampusHub.Web.Mvvm;

/// <summary>
/// Base of the pages bound to a ViewModel (the View in MVVM).
/// <list type="bullet">
/// <item>Re-renders when the ViewModel raises PropertyChanged, through InvokeAsync so updates coming from a timer
/// or any background continuation are marshalled to the renderer.</item>
/// <item>Owns the ViewModel's lifecycle: InitializeAsync when the page appears, Deactivate (cancel commands, stop
/// polling) and unsubscription when it is disposed - no leaked handler, no orphan timer.</item>
/// </list>
/// </summary>
public abstract class ViewModelComponentBase<TViewModel> : ComponentBase, IDisposable
    where TViewModel : ViewModelBase
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;

    [Inject]
    protected TViewModel ViewModel { get; set; } = default!;

    protected override void OnInitialized() => ViewModel.PropertyChanged += OnViewModelPropertyChanged;

    protected override Task OnInitializedAsync() => ViewModel.InitializeAsync(_lifetime.Token);

    // Rendering errors are handled by the renderer, so the returned task can be discarded safely.
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
        {
            return;
        }

        _disposed = true;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Deactivate();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
