using System.Diagnostics;
using Arc4u.Dependency.Attribute;
using Arc4u.MongoDB;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

/// <summary>
/// Publishes pending outbox messages. Each message is first claimed with a lease (so several instances of the
/// service can run the dispatcher), then published with its original trace as parent, then marked processed.
/// A failure is recorded with an exponential backoff; after <see cref="MaxAttempts"/> the message is parked
/// (<see cref="OutboxMessage.FailedAtUtc"/>) for an operator.
/// </summary>
[Export(typeof(IOutboxProcessor)), Scoped]
public sealed class OutboxProcessor(
    IMongoClientFactory<DatabaseContext> clientFactory,
    IOutboxPublisher publisher,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger) : IOutboxProcessor
{
    public const string ActivitySourceName = "CampusHub.Enrollment";
    public const int BatchSize = 50;
    public const int MaxAttempts = 10;
    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(30);

    private static readonly ActivitySource Source = new(ActivitySourceName);
    private static readonly string Instance = $"{Environment.MachineName}:{Environment.ProcessId}";

    private IMongoCollection<OutboxMessage> Outbox => clientFactory.GetCollection<OutboxMessage>();

    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var published = 0;
        for (var i = 0; i < BatchSize; i++)
        {
            var message = await ClaimNextAsync(cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                break; // nothing pending
            }

            if (await PublishAsync(message, cancellationToken).ConfigureAwait(false))
            {
                published++;
            }
        }

        return published;
    }

    private async Task<OutboxMessage?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var pending = Builders<OutboxMessage>.Filter.Where(m =>
            m.ProcessedAtUtc == null && m.FailedAtUtc == null && (m.LockedUntilUtc == null || m.LockedUntilUtc < now));
        var lease = Builders<OutboxMessage>.Update
            .Set(m => m.LockedUntilUtc, now + LeaseDuration)
            .Set(m => m.LockedBy, Instance);

        return await Outbox.FindOneAndUpdateAsync(pending, lease, new FindOneAndUpdateOptions<OutboxMessage>
        {
            Sort = Builders<OutboxMessage>.Sort.Ascending(m => m.OccurredAtUtc),
            ReturnDocument = ReturnDocument.After,
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        // Continue the trace of the request that produced the event (default context = new root trace).
        ActivityContext.TryParse(message.TraceParent, null, out var parent);
        using var activity = Source.StartActivity("outbox.publish", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.destination.name", message.Topic);
        activity?.SetTag("messaging.message.id", message.Id.ToString());
        activity?.SetTag("cloudevents.event_type", message.Type);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        try
        {
            await publisher.PublishAsync(message, cancellationToken).ConfigureAwait(false);

            await Outbox.UpdateOneAsync(m => m.Id == message.Id,
                Builders<OutboxMessage>.Update.Set(m => m.ProcessedAtUtc, now).Unset(m => m.LockedUntilUtc),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            logger.LogOutboxPublished(message.Id, message.Type);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var attempts = message.Attempts + 1;
            var giveUp = attempts >= MaxAttempts;
            activity?.AddException(ex);
            activity?.SetStatus(ActivityStatusCode.Error);
            logger.LogOutboxPublishFailed(ex, message.Id, attempts);

            await Outbox.UpdateOneAsync(m => m.Id == message.Id,
                Builders<OutboxMessage>.Update
                    .Set(m => m.Attempts, attempts)
                    .Set(m => m.LastError, ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message)
                    .Set(m => m.LockedUntilUtc, now + Backoff(attempts))
                    .Set(m => m.FailedAtUtc, giveUp ? now : null),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private static TimeSpan Backoff(int attempts) => TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, attempts)));
}

/// <summary>Publishes one batch of pending outbox messages; called in a loop by the host.</summary>
public interface IOutboxProcessor
{
    /// <summary>Returns how many messages were published.</summary>
    Task<int> ProcessBatchAsync(CancellationToken cancellationToken);
}
