using Arc4u.MongoDB;
using Arc4u.MongoDB.Configuration;

namespace Dev4u.CampusHub.Grading.Infrastructure.Grades;

/// <summary>
/// MongoDB persistence for the Grading service, over the CampusHub_Grading database.
/// <para>
/// This is an outward adapter: it maps the Dev4u.CampusHub.Grading.Domain entities to collections and knows nothing about
/// the use cases that read them. The database itself is not named here - it is the last segment of the
/// ConnectionStrings:CampusHub_Grading URI the Host registers the context with, so the same code targets a
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
    /// <summary>
    /// Declare one entry per collection this adapter reads or writes. The mapping is what lets a repository ask
    /// for <c>IMongoClientFactory&lt;DatabaseContext&gt;.GetCollection&lt;TEntity&gt;()</c> without repeating the
    /// collection name, so adding an entity is a line here and a new repository - this method is the only place
    /// that knows how entities and collections line up.
    /// </summary>
    protected override void OnConfiguring(DbContextBuilder context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // context.MapCollection("<collectionname>").With<Dev4u.CampusHub.Grading.Domain.<Entity>>();
    }
}
