using System.Diagnostics.CodeAnalysis;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDBMigrations;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments.MigrationTool.Migrations;

/// <summary>
/// The first migration of the CampusHub_Enrollment database, and the template for the next ones: copy it, bump
/// <see cref="Version"/>, and leave this one alone once it has run anywhere.
/// <para>
/// Collections are addressed as BsonDocument rather than through the CampusHub.Enrollment.Infrastructure.Enrollments entities. That
/// is what keeps a migration replayable: it reshapes documents written by an older version of an entity,
/// which the current mapping can no longer deserialize - and the adapter's 3.x MongoDB driver cannot be
/// loaded in this project anyway (see the csproj).
/// </para>
/// <para>
/// It stays internal: the engine discovers migrations by scanning this assembly's defined types, which
/// reaches non-public ones, and nothing outside the tool ever constructs one.
/// </para>
/// </summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "The migration engine instantiates this class by reflection after scanning the assembly, " +
                    "so no call site is visible to the analyzer.")]
internal sealed class InitialMigration : IMigration
{
    /// <summary>
    /// Ordering is by version, not by file name, and the engine records it in
    /// Enrollments__MigrationsHistory. Two migrations must never share one.
    /// </summary>
    public MongoDBMigrations.Version Version => new(1, 0, 0);

    public string Name => "Initialization";

    /// <summary>
    /// What this migration does when the database moves up to <see cref="Version"/>. It runs once per
    /// database, so it has to be safe on a database that already holds documents: CreateOne on an index that
    /// exists with the same keys and options is a no-op, an unconditional InsertOne is not.
    /// </summary>
    public void Up(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);

        // The indexes the Enrollment queries need. One method per collection keeps the diff readable
        // when the next migration touches only one of them.
        //
        // ExampleUp(database.GetCollection<BsonDocument>("<collectionname>"));
    }

    /// <summary>
    /// The way back out, for a rollback to the version below this one. Leave it empty when there is nothing
    /// to undo - dropping an index is cheap, dropping data the previous release cannot read back is not.
    /// </summary>
    public void Down(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
    }

    // private static void ExampleUp(IMongoCollection<BsonDocument> collection)
    // {
    //     collection.Indexes.CreateOne(new CreateIndexModel<BsonDocument>(
    //         Builders<BsonDocument>.IndexKeys.Ascending("Name"),
    //         new CreateIndexOptions { Background = true, Unique = true, Sparse = true }));
    // }
}
