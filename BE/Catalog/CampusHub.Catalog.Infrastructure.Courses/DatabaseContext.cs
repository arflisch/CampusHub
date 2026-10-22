using Arc4u.MongoDB;
using Arc4u.MongoDB.Configuration;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Dev4u.CampusHub.Catalog.Infrastructure.Courses;

/// <summary>
/// MongoDB persistence for the Catalog service, over the CampusHub_Catalog database.
/// <para>
/// This is an outward adapter: it maps the Dev4u.CampusHub.Catalog.Domain entities to collections and knows nothing about
/// the use cases that read them. The database itself is not named here - it is the last segment of the
/// ConnectionStrings:CampusHub_Catalog URI the Host registers the context with, so the same code targets a
/// different database per environment.
/// </para>
/// <para>
/// The context is instantiated by <c>AddMongoDatabase&lt;DatabaseContext&gt;</c> and therefore must keep its
/// parameterless constructor.
/// </para>
/// </summary>
public class DatabaseContext : DbContext
{
    public const string CoursesCollection = "courses";

    protected override void OnConfiguring(DbContextBuilder context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // MongoDB.Driver 3.x no longer guesses how to store a Guid: store them as standard UUIDs.
        BsonSerializer.TryRegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

        context.MapCollection(CoursesCollection).With<CourseDocument>();
    }
}
