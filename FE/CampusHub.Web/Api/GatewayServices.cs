using Dev4u.CampusHub.Web.Presentation.Models;
using Dev4u.CampusHub.Web.Presentation.Services;
using FluentResults;

namespace Dev4u.CampusHub.Web.Api;

// Adapters of the presentation ports over the gateway. The wire records mirror the JSON of the services'
// frontend APIs and are mapped to the UI models here, so a contract change stays local to this file.

internal sealed record CourseWire(Guid Id, string Code, string Title, string TeacherId, int Capacity, int SeatsLeft, int PriceInCents,
                                   DateTimeOffset StartsAt, DateTimeOffset EndsAt, string Status, DateTimeOffset? PublishedAt)
{
    public CourseItem ToItem() =>
        new(Id, Code, Title, TeacherId, Capacity, SeatsLeft, PriceInCents, StartsAt, EndsAt,
            Enum.Parse<CourseState>(Status, ignoreCase: true), PublishedAt);
}

internal sealed record EnrollmentWire(Guid Id, Guid CourseId, string? CourseTitle, DateTimeOffset? CourseStartsAt, int CoursePriceInCents,
                                       string Status, string? RejectionCode, string? CancellationReason, DateTimeOffset RequestedAt,
                                       DateTimeOffset UpdatedAt)
{
    public EnrollmentItem ToItem() =>
        new(Id, CourseId, CourseTitle, CourseStartsAt, CoursePriceInCents, Enum.Parse<EnrollmentState>(Status, ignoreCase: true),
            RejectionCode, RequestedAt, UpdatedAt, CancellationReason);
}

internal sealed record ParticipantWire(Guid EnrollmentId, string StudentId, string? StudentName, string Status, int CoursePriceInCents,
                                        DateTimeOffset RequestedAt)
{
    public ParticipantItem ToItem() =>
        new(EnrollmentId, StudentId, StudentName, Enum.Parse<EnrollmentState>(Status, ignoreCase: true), CoursePriceInCents, RequestedAt);
}

internal sealed record PaymentWire(Guid EnrollmentId, Guid CourseId, int AmountInCents, string Currency, string Status, string? DeclineReason,
                                    DateTimeOffset CreatedAt, DateTimeOffset? RefundedAt)
{
    public PaymentItem ToItem() =>
        new(EnrollmentId, CourseId, AmountInCents, Enum.Parse<PaymentState>(Status, ignoreCase: true), DeclineReason, CreatedAt, RefundedAt);
}

internal sealed record CertificateWire(string Number, Guid EnrollmentId, Guid CourseId, string CourseTitle, DateTimeOffset CourseEndedAt,
                                        DateTimeOffset IssuedAt);

internal sealed record NotificationWire(Guid Id, string Title, string Message, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

internal sealed record CurrentUserWire(string UserId, string DisplayName, bool IsTeacher, bool IsAdmin);

internal sealed class GatewayCourseCatalog(IHttpClientFactory httpClientFactory) : ICourseCatalog
{
    private const string Courses = "catalog/frontend/courses";

    private HttpClient Http => httpClientFactory.CreateClient(GatewayBackend.ClientName);

    public Task<Result<IReadOnlyList<CourseItem>>> ListPublishedAsync(CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<List<CourseWire>, IReadOnlyList<CourseItem>>(Http, () => GatewayApi.Get($"{Courses}?take=100"),
            wire => [.. wire.Select(c => c.ToItem())], cancellationToken);

    public Task<Result<IReadOnlyList<CourseItem>>> ListMineAsync(CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<List<CourseWire>, IReadOnlyList<CourseItem>>(Http, () => GatewayApi.Get($"{Courses}/mine"),
            wire => [.. wire.Select(c => c.ToItem())], cancellationToken);

    public Task<Result<CourseItem>> CreateAsync(string code, string title, int capacity, int priceInCents, DateTimeOffset startsAt,
                                                DateTimeOffset endsAt, CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<CourseWire, CourseItem>(Http, () => GatewayApi.Post(Courses, new { code, title, capacity, priceInCents, startsAt, endsAt }),
            wire => wire.ToItem(), cancellationToken);

    public Task<Result<CourseItem>> PublishAsync(Guid courseId, CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<CourseWire, CourseItem>(Http, () => GatewayApi.Post($"{Courses}/{courseId}/publish"),
            wire => wire.ToItem(), cancellationToken);

    public Task<Result<CourseItem>> CancelAsync(Guid courseId, CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<CourseWire, CourseItem>(Http, () => GatewayApi.Post($"{Courses}/{courseId}/cancel"),
            wire => wire.ToItem(), cancellationToken);
}

internal sealed class GatewayEnrollmentService(IHttpClientFactory httpClientFactory) : IEnrollmentService
{
    private const string Enrollments = "enrollment/frontend/enrollments";

    private HttpClient Http => httpClientFactory.CreateClient(GatewayBackend.ClientName);

    public Task<Result<EnrollmentItem>> RequestAsync(Guid requestId, Guid courseId, CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<EnrollmentWire, EnrollmentItem>(Http, () => GatewayApi.Post(Enrollments, new { requestId, courseId }),
            wire => wire.ToItem(), cancellationToken);

    public Task<Result<IReadOnlyList<EnrollmentItem>>> ListMineAsync(CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<List<EnrollmentWire>, IReadOnlyList<EnrollmentItem>>(Http, () => GatewayApi.Get(Enrollments),
            wire => [.. wire.Select(e => e.ToItem())], cancellationToken);

    public Task<Result<EnrollmentItem>> CancelAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<EnrollmentWire, EnrollmentItem>(Http, () => GatewayApi.Post($"{Enrollments}/{enrollmentId}/cancel"),
            wire => wire.ToItem(), cancellationToken);

    public Task<Result<IReadOnlyList<ParticipantItem>>> ListParticipantsAsync(Guid courseId, CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<List<ParticipantWire>, IReadOnlyList<ParticipantItem>>(Http,
            () => GatewayApi.Get($"enrollment/frontend/courses/{courseId}/participants"), wire => [.. wire.Select(p => p.ToItem())], cancellationToken);
}

internal sealed class GatewayNotificationService(IHttpClientFactory httpClientFactory) : INotificationService
{
    public Task<Result<IReadOnlyList<NotificationItem>>> ListMineAsync(CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<List<NotificationWire>, IReadOnlyList<NotificationItem>>(
            httpClientFactory.CreateClient(GatewayBackend.ClientName), () => GatewayApi.Get("notification/frontend/notifications"),
            wire => [.. wire.Select(n => new NotificationItem(n.Id, n.Title, n.Message, n.CreatedAt, n.ReadAt is not null))],
            cancellationToken);

    public Task<Result> MarkAllReadAsync(CancellationToken cancellationToken) =>
        GatewayApi.SendAsync(httpClientFactory.CreateClient(GatewayBackend.ClientName), () => GatewayApi.Post("notification/frontend/notifications/read"),
            cancellationToken);
}

internal sealed class GatewayAccountService(IHttpClientFactory httpClientFactory) : IAccountService
{
    private HttpClient Http => httpClientFactory.CreateClient(GatewayBackend.ClientName);

    public Task<Result<IReadOnlyList<PaymentItem>>> ListPaymentsAsync(CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<List<PaymentWire>, IReadOnlyList<PaymentItem>>(Http, () => GatewayApi.Get("billing/frontend/payments"),
            wire => [.. wire.Select(p => p.ToItem())], cancellationToken);

    public Task<Result<IReadOnlyList<CertificateItem>>> ListCertificatesAsync(CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<List<CertificateWire>, IReadOnlyList<CertificateItem>>(Http, () => GatewayApi.Get("grading/frontend/certificates"),
            wire => [.. wire.Select(c => new CertificateItem(c.Number, c.CourseId, c.CourseTitle, c.CourseEndedAt, c.IssuedAt))], cancellationToken);
}

internal sealed class GatewayCurrentUserService(IHttpClientFactory httpClientFactory) : ICurrentUserService
{
    public Task<Result<CurrentUser>> GetAsync(CancellationToken cancellationToken) =>
        GatewayApi.SendAsync<CurrentUserWire, CurrentUser>(
            httpClientFactory.CreateClient(GatewayBackend.ClientName), () => GatewayApi.Get("frontend/me"),
            wire => new CurrentUser(wire.UserId, wire.DisplayName, wire.IsTeacher, wire.IsAdmin), cancellationToken);
}

internal static class GatewayBackend
{
    public const string ClientName = "Gateway";

    public static IServiceCollection AddGatewayBackend(this IServiceCollection services, Uri baseAddress)
    {
        services.AddTransient<GatewayMessageHandler>();
        services.AddHttpClient(ClientName, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddHttpMessageHandler<GatewayMessageHandler>();

        services.AddScoped<ICourseCatalog, GatewayCourseCatalog>();
        services.AddScoped<IEnrollmentService, GatewayEnrollmentService>();
        services.AddScoped<INotificationService, GatewayNotificationService>();
        services.AddScoped<ICurrentUserService, GatewayCurrentUserService>();
        services.AddScoped<IAccountService, GatewayAccountService>();
        services.AddSingleton<ILiveUpdates>(_ => new SignalRLiveUpdates(new Uri(baseAddress, "notification/hubs/campus")));
        return services;
    }
}
