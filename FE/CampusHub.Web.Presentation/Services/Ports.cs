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
}

public interface IEnrollmentService
{
    /// <summary>Idempotent on <paramref name="requestId"/>: sending the same request twice returns the same enrollment.</summary>
    Task<Result<EnrollmentItem>> RequestAsync(Guid requestId, Guid courseId, CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<EnrollmentItem>>> ListMineAsync(CancellationToken cancellationToken);
}

public interface INotificationService
{
    Task<Result<IReadOnlyList<NotificationItem>>> ListMineAsync(CancellationToken cancellationToken);
}

public interface ICurrentUserService
{
    Task<Result<CurrentUser>> GetAsync(CancellationToken cancellationToken);
}
