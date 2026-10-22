using Arc4u.MongoDB;
using Arc4u.MongoDB.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Dev4u.CampusHub.Notification.Infrastructure.Notifications;

/// <summary>
/// MongoDB persistence for the Notification service, over the CampusHub_Notification database.
/// The context is instantiated by <c>AddMongoDatabase&lt;DatabaseContext&gt;</c> and therefore must keep its
/// parameterless constructor.
/// </summary>
public class DatabaseContext : DbContext
{
    public const string NotificationsCollection = "notifications";

    protected override void OnConfiguring(DbContextBuilder context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // MongoDB.Driver 3.x no longer guesses how to store a Guid: store them as standard UUIDs.
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

        context.MapCollection(NotificationsCollection).With<NotificationDocument>();
    }
}
