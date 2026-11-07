using AwesomeAssertions;
using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Services;
using Dev4u.CampusHub.Web.Presentation.ViewModels;
using FluentResults;
using Xunit;

namespace Dev4u.CampusHub.Web.Presentation.Tests;

internal sealed class FakeNotificationService : INotificationService
{
    public List<NotificationItem> Items { get; } = [];

    public int MarkAllReadCalls { get; private set; }

    public Task<Result<IReadOnlyList<NotificationItem>>> ListMineAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result.Ok<IReadOnlyList<NotificationItem>>([.. Items]));

    public Task<Result> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        MarkAllReadCalls++;
        return Task.FromResult(Result.Ok());
    }
}

public class NotificationsViewModelTests
{
    private readonly FakeNotificationService _notifications = new();
    private readonly FakeLiveUpdates _live = new();
    private readonly NotificationCounter _counter = new();

    private static NotificationItem Notification(bool isRead) => new(Guid.NewGuid(), "Enrollment confirmed", "…", DateTimeOffset.UnixEpoch, isRead);

    [Fact]
    public async Task Marking_all_as_read_clears_the_unread_count_shown_in_the_menu()
    {
        _notifications.Items.AddRange([Notification(isRead: false), Notification(isRead: false), Notification(isRead: true)]);
        var sut = new NotificationsViewModel(_notifications, _live, _counter);
        await sut.InitializeAsync(CancellationToken.None);
        _counter.Unread.Should().Be(2);

        await sut.MarkAllReadCommand.ExecuteAsync(null);

        _counter.Unread.Should().Be(0);
        sut.Notifications.Should().OnlyContain(n => n.IsRead);
        sut.MarkAllReadCommand.CanExecute(null).Should().BeFalse("nothing left to mark");
    }

    [Fact]
    public async Task A_pushed_notification_appears_first_and_is_counted_by_the_shell()
    {
        var page = new NotificationsViewModel(_notifications, _live, _counter);
        var shell = new ShellViewModel(new FakeCurrentUserService(new CurrentUser("u1", "Sam")), _notifications, _live, _counter);
        await shell.InitializeAsync(CancellationToken.None);
        await page.InitializeAsync(CancellationToken.None);
        var pushed = Notification(isRead: false);

        _live.Push(pushed);

        page.Notifications.Should().StartWith(pushed);
        shell.UnreadCount.Should().Be(1);
    }
}

internal sealed class FakeAccountService : IAccountService
{
    public Result<IReadOnlyList<PaymentItem>> Payments { get; set; } = Result.Ok<IReadOnlyList<PaymentItem>>([]);

    public Result<IReadOnlyList<CertificateItem>> Certificates { get; set; } = Result.Ok<IReadOnlyList<CertificateItem>>([]);

    public Task<Result<IReadOnlyList<PaymentItem>>> ListPaymentsAsync(CancellationToken cancellationToken) => Task.FromResult(Payments);

    public Task<Result<IReadOnlyList<CertificateItem>>> ListCertificatesAsync(CancellationToken cancellationToken) => Task.FromResult(Certificates);
}

public class AccountViewModelTests
{
    [Fact]
    public async Task Payments_and_certificates_load_side_by_side_and_one_failing_does_not_hide_the_other()
    {
        var account = new FakeAccountService
        {
            Payments = Result.Ok<IReadOnlyList<PaymentItem>>(
            [
                new PaymentItem(Guid.NewGuid(), Guid.NewGuid(), 4900, PaymentState.Captured, null, DateTimeOffset.UnixEpoch, null),
                new PaymentItem(Guid.NewGuid(), Guid.NewGuid(), 9900, PaymentState.Refunded, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            ]),
            Certificates = TestData.Transient<IReadOnlyList<CertificateItem>>(),
        };
        var sut = new AccountViewModel(account);

        await sut.InitializeAsync(CancellationToken.None);

        sut.Payments.Should().HaveCount(2);
        sut.TotalPaidInCents.Should().Be(4900, "a refunded payment no longer counts");
        sut.ErrorMessage.Should().NotBeNull("the certificates could not be loaded");
    }
}
