using Arc4u.MongoDB;
using Arc4u.MongoDB.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

/// <summary>
/// MongoDB persistence for the Enrollment service, over the CampusHub_Enrollment database.
/// <para>
/// This is an outward adapter: it maps the Dev4u.CampusHub.Enrollment.Domain entities to collections and knows nothing about
/// the use cases that read them. The database itself is not named here - it is the last segment of the
/// ConnectionStrings:CampusHub_Enrollment URI the Host registers the context with.
/// </para>
/// <para>
/// The context is instantiated by <c>AddMongoDatabase&lt;DatabaseContext&gt;</c> and therefore must keep its
/// parameterless constructor. The outbox requires multi-document transactions, hence a replica set.
/// </para>
/// </summary>
public class DatabaseContext : DbContext
{
    public const string EnrollmentsCollection = "enrollments";
    public const string OutboxCollection = "outbox";

    protected override void OnConfiguring(DbContextBuilder context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // MongoDB.Driver 3.x no longer guesses how to store a Guid: store them as standard UUIDs.
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

        context.MapCollection(EnrollmentsCollection).With<EnrollmentDocument>();
        context.MapCollection(OutboxCollection).With<OutboxMessage>();
    }
}
