using Arc4u.Dependency.Attribute;
using Arc4u.MongoDB;
using Dev4u.CampusHub.Notification.Domain.Notifications;
using Microsoft.Extensions.Logging;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace Dev4u.CampusHub.Notification.Infrastructure.Notifications;

[BsonIgnoreExtraElements]
public sealed class NotificationDocument
{
    /// <summary>Id of the source integration event: the primary key doubles as the inbox.</summary>
    [BsonId]
    public Guid Id { get; set; }

    public string RecipientId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? ReadAtUtc { get; set; }
}

/// <summary>MongoDB implementation of <see cref="INotificationRepository"/>.</summary>
[Export(typeof(INotificationRepository)), Scoped]
public sealed class MongoNotificationRepository(IMongoClientFactory<DatabaseContext> clientFactory, ILogger<MongoNotificationRepository> logger)
    : INotificationRepository
{
    private IMongoCollection<NotificationDocument> Notifications => clientFactory.GetCollection<NotificationDocument>();

    public async Task<bool> AddIfAbsentAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        try
        {
            await Notifications.InsertOneAsync(new NotificationDocument
            {
                Id = notification.Id,
                RecipientId = notification.RecipientId,
                Title = notification.Title,
                Message = notification.Message,
                CreatedAtUtc = notification.CreatedAt.UtcDateTime,
                ReadAtUtc = notification.ReadAt?.UtcDateTime,
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            logger.LogDocumentsPersisted(1);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            return false; // the event was already processed
        }
    }

    public async Task<IReadOnlyList<UserNotification>> ListForRecipientAsync(string recipientId, int take, CancellationToken cancellationToken)
    {
        var documents = await Notifications.Find(n => n.RecipientId == recipientId)
                                           .SortByDescending(n => n.CreatedAtUtc)
                                           .Limit(take)
                                           .ToListAsync(cancellationToken)
                                           .ConfigureAwait(false);
        return [.. documents.Select(d => UserNotification.Rehydrate(d.Id, d.RecipientId, d.Title, d.Message,
                                                                    AsUtc(d.CreatedAtUtc), d.ReadAtUtc is { } read ? AsUtc(read) : null))];
    }

    private static DateTimeOffset AsUtc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}

/// <summary>Indexes of the Notification database, created idempotently at startup by the host.</summary>
public static class NotificationIndexes
{
    public static Task EnsureAsync(IMongoClientFactory<DatabaseContext> clientFactory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        return clientFactory.GetCollection<NotificationDocument>().Indexes.CreateOneAsync(
            new CreateIndexModel<NotificationDocument>(
                Builders<NotificationDocument>.IndexKeys.Ascending(n => n.RecipientId).Descending(n => n.CreatedAtUtc),
                new CreateIndexOptions { Name = "recipient_latest" }),
            cancellationToken: cancellationToken);
    }
}
