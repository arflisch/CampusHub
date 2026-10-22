using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.AspNetCore.Components;

namespace Dev4u.CampusHub.Web.Components;

public partial class CommandButton : IDisposable
{
    private IAsyncRelayCommand? _subscribed;
    private bool _disposed;

    [Parameter, EditorRequired] public IAsyncRelayCommand Command { get; set; } = default!;

    [Parameter] public object? Parameter { get; set; }

    [Parameter] public RenderFragment? ChildContent { get; set; }

    [Parameter] public string CssClass { get; set; } = "btn";

    [Parameter] public string? BusyText { get; set; }

    [Parameter] public string? Title { get; set; }

    [Parameter] public string Type { get; set; } = "button";

    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_subscribed, Command))
        {
            return;
        }

        Unsubscribe();
        Command.CanExecuteChanged += OnCommandChanged;
        Command.PropertyChanged += OnCommandPropertyChanged; // IsRunning
        _subscribed = Command;
    }

    private async Task ExecuteAsync()
    {
        if (Command.CanExecute(Parameter))
        {
            await Command.ExecuteAsync(Parameter);
        }
    }

    private void OnCommandChanged(object? sender, EventArgs e) => _ = InvokeAsync(StateHasChanged);

    private void OnCommandPropertyChanged(object? sender, PropertyChangedEventArgs e) => _ = InvokeAsync(StateHasChanged);

    private void Unsubscribe()
    {
        if (_subscribed is null)
        {
            return;
        }

        _subscribed.CanExecuteChanged -= OnCommandChanged;
        _subscribed.PropertyChanged -= OnCommandPropertyChanged;
        _subscribed = null;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            Unsubscribe();
            _disposed = true;
        }
    }
}
