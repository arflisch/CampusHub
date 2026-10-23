using Arc4u.MongoDB;
using Arc4u.MongoDB.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Dev4u.CampusHub.Billing.Infrastructure.Payments;

/// <summary>
/// MongoDB persistence for the Billing service, over the CampusHub_Billing database.
/// <para>
/// This is an outward adapter: it maps the Dev4u.CampusHub.Billing.Domain entities to collections and knows nothing about
/// the use cases that read them. The database itself is not named here - it is the last segment of the
/// ConnectionStrings:CampusHub_Billing URI the Host registers the context with, so the same code targets a
/// different database per environment.
/// </para>
/// <para>
/// The context is instantiated by <c>AddMongoDatabase&lt;DatabaseContext&gt;</c> and therefore must keep its
/// parameterless constructor. MongoDB is schemaless: there is no migration tool to run, a collection comes into
/// existence on its first write.
/// </para>
/// </summary>
public class DatabaseContext : DbContext
{
    public const string PaymentsCollection = "payments";

    /// <summary>
    /// Declare one entry per collection this adapter reads or writes. The mapping is what lets a repository ask
    /// for <c>IMongoClientFactory&lt;DatabaseContext&gt;.GetCollection&lt;TEntity&gt;()</c> without repeating the
    /// collection name, so adding an entity is a line here and a new repository - this method is the only place
    /// that knows how entities and collections line up.
    /// </summary>
    protected override void OnConfiguring(DbContextBuilder context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // MongoDB.Driver 3.x no longer guesses how to store a Guid: store them as standard UUIDs.
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

        context.MapCollection(PaymentsCollection).With<PaymentDocument>();
    }
}
