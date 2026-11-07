using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Mvvm;
using Dev4u.CampusHub.Web.Presentation.Services;

namespace Dev4u.CampusHub.Web.Presentation.ViewModels;

/// <summary>What the student owns: payments (Billing) and certificates (Grading), loaded side by side.</summary>
public sealed partial class AccountViewModel : ViewModelBase
{
    private readonly IAccountService _account;

    public AccountViewModel(IAccountService account)
    {
        _account = account;
        Payments = [];
        Certificates = [];
    }

    [ObservableProperty]
    public partial IReadOnlyList<PaymentItem> Payments { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<CertificateItem> Certificates { get; private set; }

    /// <summary>Total paid and not refunded, in cents.</summary>
    public int TotalPaidInCents => Payments.Where(p => p.State == PaymentState.Captured).Sum(p => p.AmountInCents);

    public override Task InitializeAsync(CancellationToken cancellationToken) => LoadCommand.ExecuteAsync(null);

    public override void Deactivate() => LoadCommand.Cancel();

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        ErrorMessage = null;
        var payments = _account.ListPaymentsAsync(cancellationToken);
        var certificates = _account.ListCertificatesAsync(cancellationToken);
        await Task.WhenAll(payments, certificates);

        // Each half is shown even if the other service is down.
        var paid = await payments;
        if (paid.IsSuccess)
        {
            Payments = paid.Value;
            OnPropertyChanged(nameof(TotalPaidInCents));
        }
        else
        {
            ShowErrors(paid.Errors);
        }

        var earned = await certificates;
        if (earned.IsSuccess)
        {
            Certificates = earned.Value;
        }
        else
        {
            ShowErrors(earned.Errors);
        }
    }
}
