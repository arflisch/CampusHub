using Dev4u.CampusHub.Web.Presentation.Models;
using FluentResults;

namespace Dev4u.CampusHub.Web.Presentation.Services;

// Ports used by the ViewModels. They are implemented over the gateway (HTTP, cookie session) by CampusHub.Web, and
// in memory for the demo mode and the unit tests. Every method returns a Result: expected failures (course full,
// server unreachable...) are data the ViewModel shows, not exceptions.

public interface ICourseCatalog
{
    Task<Result<IReadOnlyList<CourseItem>>> ListPublishedAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<CourseItem>>> ListMineAsync(CancellationToken cancellationToken);

    Task<Result<CourseItem>> CreateAsync(string code, string title, int capacity, int priceInCents, DateTimeOffset startsAt,
                                         DateTimeOffset endsAt, CancellationToken cancellationToken);

    Task<Result<CourseItem>> PublishAsync(Guid courseId, CancellationToken cancellationToken);

    /// <summary>Cancels one of my courses before it starts; its students are refunded and told.</summary>
    Task<Result<CourseItem>> CancelAsync(Guid courseId, CancellationToken cancellationToken);
}

public interface IEnrollmentService
{
    /// <summary>Idempotent on <paramref name="requestId"/>: sending the same request twice returns the same enrollment.</summary>
    Task<Result<EnrollmentItem>> RequestAsync(Guid requestId, Guid courseId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<EnrollmentItem>>> ListMineAsync(CancellationToken cancellationToken);

    /// <summary>Cancels one of my confirmed enrollments before the course starts; the payment is refunded.</summary>
    Task<Result<EnrollmentItem>> CancelAsync(Guid enrollmentId, CancellationToken cancellationToken);

    /// <summary>The participants of one of my courses (teachers).</summary>
    Task<Result<IReadOnlyList<ParticipantItem>>> ListParticipantsAsync(Guid courseId, CancellationToken cancellationToken);
}

public interface INotificationService
{
    Task<Result<IReadOnlyList<NotificationItem>>> ListMineAsync(CancellationToken cancellationToken);

    Task<Result> MarkAllReadAsync(CancellationToken cancellationToken);
}

/// <summary>What the student owns: payments (Billing) and certificates (Grading).</summary>
public interface IAccountService
{
    Task<Result<IReadOnlyList<PaymentItem>>> ListPaymentsAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<CertificateItem>>> ListCertificatesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Changes pushed by the server (SignalR over the gateway): the screens refresh at once instead of waiting for a
/// reload. One connection for the whole application, started by the shell; events are raised on the UI context.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1003:Use generic event handler instances",
    Justification = "The payloads are immutable records; EventHandler<T> no longer requires an EventArgs.")]
public interface ILiveUpdates
{
    event EventHandler<EnrollmentChange>? EnrollmentChanged;

    event EventHandler<NotificationItem>? NotificationReceived;

    bool IsConnected { get; }

    Task StartAsync(CancellationToken cancellationToken);
}

public interface ICurrentUserService
{
    Task<Result<CurrentUser>> GetAsync(CancellationToken cancellationToken);
}
