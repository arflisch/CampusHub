using System.Security.Cryptography;
using System.Text;
using Arc4u.Dependency.Attribute;
using Dapr;
using Dapr.Client;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using Dev4u.CampusHub.Catalog.IntegrationEvents;
using Dev4u.CampusHub.Catalog.UseCases.Courses;
using Dev4u.CampusHub.Errors;
using FluentResults;
using Grpc.Core;

namespace Dev4u.CampusHub.Catalog.Host.Infrastructure;

/// <summary>Publishes CourseCancelled on the Catalog pub/sub component (RabbitMQ) through the Dapr sidecar.</summary>
[Export(typeof(ICourseCancellationAnnouncer)), Scoped]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the Arc4u dependency injection ([Export]).")]
internal sealed partial class DaprCourseCancellationAnnouncer(DaprClient dapr, ILogger<DaprCourseCancellationAnnouncer> logger)
    : ICourseCancellationAnnouncer
{
    /// <summary>Name of the Dapr pub/sub component scoped to this service (Dapr/*/CampusHub-Catalog-pubsub-PubSub.yaml).</summary>
    public const string PubSubName = "campushub-catalog-pubsub-pubsub";

    public async Task<Result> AnnounceAsync(Course course, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(course);
        if (course.Cancellation is not { } cancellation)
        {
            return Result.Ok();
        }

        var integrationEvent = new CourseCancelledV1(DeterministicId(course.Id, CatalogEvents.CourseCancelledV1Type),
                                                     cancellation.CancelledAt, course.Id, course.Title);
        try
        {
            await dapr.PublishEventAsync(PubSubName, CatalogEvents.Topic, integrationEvent, new Dictionary<string, string>
            {
                // Stable id: subscribers deduplicate on it. Type: matched by the subscriptions' routing rules.
                ["cloudevent.id"] = integrationEvent.EventId.ToString(),
                ["cloudevent.type"] = CatalogEvents.CourseCancelledV1Type,
            }, cancellationToken).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is DaprException or RpcException or HttpRequestException)
        {
            LogAnnouncementFailed(logger, ex, course.Id);
            return Result.Fail(AppErrors.Unavailable("course.announcement_failed", "The cancellation could not be published; it will be retried."));
        }
    }

    private static Guid DeterministicId(Guid aggregateId, string eventType)
    {
        Span<byte> bytes = stackalloc byte[16];
        aggregateId.TryWriteBytes(bytes);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(eventType));
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= digest[i];
        }

        return new Guid(bytes);
    }

    [LoggerMessage(EventId = 4300, Level = LogLevel.Warning, Message = "The cancellation of course {CourseId} could not be announced.")]
    private static partial void LogAnnouncementFailed(ILogger logger, Exception exception, Guid courseId);
}

/// <summary>
/// Announces the cancelled courses every few seconds (state-based outbox: the course itself records whether its
/// cancellation was published). One DI scope per iteration; a failed iteration is logged, never fatal.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by AddHostedService in Program.cs.")]
internal sealed partial class CourseCancellationDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<CourseCancellationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using var _ = scope.ConfigureAwait(false);
                    await scope.ServiceProvider.GetRequiredService<AnnounceCourseCancellations>().ExecuteAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogIterationFailed(logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    [LoggerMessage(EventId = 4301, Level = LogLevel.Error, Message = "Course cancellation dispatch iteration failed.")]
    private static partial void LogIterationFailed(ILogger logger, Exception exception);
}
