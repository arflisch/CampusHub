using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev4u.CampusHub.Web.Presentation.Errors;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Mvvm;
using Dev4u.CampusHub.Web.Presentation.Services;

namespace Dev4u.CampusHub.Web.Presentation.ViewModels;

/// <summary>
/// The student's enrollments. While at least one saga is still running (Requested or SeatHeld), the list is polled
/// every <see cref="PollInterval"/> until every enrollment is settled; leaving the page cancels the polling.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Disposed in Deactivate. ViewModels are not IDisposable on purpose: the DI container would keep every transient disposable alive until the app closes.")]
public sealed partial class MyEnrollmentsViewModel : ViewModelBase
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IEnrollmentService _enrollments;
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _polling;

    public MyEnrollmentsViewModel(IEnrollmentService enrollments, TimeProvider timeProvider)
    {
        _enrollments = enrollments;
        _timeProvider = timeProvider;
        Enrollments = [];
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<EnrollmentItem> Enrollments { get; private set; }

    [ObservableProperty]
    public partial bool IsPolling { get; private set; }

    public bool IsEmpty => Enrollments.Count == 0;

    /// <summary>The running polling loop, if any (exposed for tests).</summary>
    internal Task? PollingTask { get; private set; }

    public override async Task InitializeAsync(CancellationToken cancellationToken) => await LoadCommand.ExecuteAsync(null);

    public override void Deactivate()
    {
        LoadCommand.Cancel();
        _polling?.Cancel();
        _polling?.Dispose();
        _polling = null;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var result = await _enrollments.ListMineAsync(cancellationToken);
        if (result.IsFailed)
        {
            ShowErrors(result.Errors);
            return;
        }

        Enrollments = result.Value;
        EnsurePolling();
    }

    private void EnsurePolling()
    {
        if (PollingTask is { IsCompleted: false } || Enrollments.All(e => e.IsSettled))
        {
            return;
        }

        _polling?.Dispose();
        _polling = new CancellationTokenSource();
        PollingTask = PollUntilSettledAsync(_polling.Token);
    }

    private async Task PollUntilSettledAsync(CancellationToken cancellationToken)
    {
        IsPolling = true;
        try
        {
            using var timer = new PeriodicTimer(PollInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var result = await _enrollments.ListMineAsync(cancellationToken);
                if (result.IsFailed)
                {
                    if (result.Errors.AreTransient())
                    {
                        continue; // keep polling: the server may be back at the next tick
                    }

                    ShowErrors(result.Errors);
                    return;
                }

                Enrollments = result.Value;
                if (Enrollments.All(e => e.IsSettled))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The page was left.
        }
        finally
        {
            IsPolling = false;
        }
    }

    /// <summary>Text explaining a rejection, from its code.</summary>
    public static string DescribeRejection(EnrollmentItem enrollment) =>
        enrollment is null ? string.Empty : UiErrors.ForCode(enrollment.RejectionCode) ?? enrollment.RejectionCode ?? "Rejected.";
}
