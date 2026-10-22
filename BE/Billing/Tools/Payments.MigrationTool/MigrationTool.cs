using System.Reflection;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDBMigrations;
using MongoDBMigrations.Core;
using MongoDBMigrations.Document;

namespace Dev4u.CampusHub.Billing.Infrastructure.Payments.MigrationTool;

/// <summary>
/// The commands the tool exposes, over the CampusHub_Billing database behind ConnectionStrings:CampusHub_Billing.
/// <para>
/// Every migration is a class in Migrations/ implementing <see cref="IMigration"/>, and this assembly is the
/// one the engine scans - the adapter the service ships stays free of them. What has already run is recorded
/// in the <c>Payments__MigrationsHistory</c> collection of the target database, which is also
/// where the list commands read from, so the tool never guesses at the state of a database it did not write.
/// </para>
/// </summary>
internal sealed class MigrationTool(IConfiguration configuration)
{
    /// <summary>
    /// The collection the applied versions are recorded in. It carries the project name rather than
    /// MongoDBMigrations' own default, so a database shared by two adapters keeps one history per adapter -
    /// the same reason the EF Core tool prefixes its __MigrationsHistory table.
    /// </summary>
    private const string HistoryCollection = "Payments__MigrationsHistory";

    /// <summary>
    /// The serializer the history documents need to read their version back. BsonSerializer.RegisterSerializer
    /// throws on a second registration for the same type, so it is done once here.
    /// </summary>
    private static readonly bool _versionSerializerRegistered = RegisterVersionSerializer();

    public void Apply(CommandLineApplication cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        cmd.HelpOption("-? | -h | --help");
        cmd.Description = "Migrate the database up to a specific version.";
        var version = cmd.Option("-v | --version <VERSION>", "The version to migrate to, e.g. 1.2.0.", CommandOptionType.SingleValue)
                         .IsRequired();

        cmd.OnExecute(() => Run(() => Migrate(version.Value())));
    }

    public void ApplyAll(CommandLineApplication cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        cmd.HelpOption("-? | -h | --help");
        cmd.Description = "Migrate the database to the latest version.";
        cmd.OnExecute(() => Run(() => Migrate(null)));
    }

    public void ListPending(CommandLineApplication cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        cmd.HelpOption("-? | -h | --help");
        cmd.Description = "Show the migrations that have not been applied yet.";
        cmd.OnExecute(() => Run(() =>
        {
            var url = DatabaseUrl();
            Write(ConsoleColor.Red, $"List the pending migration(s) for database {url.DatabaseName}.");

            var applied = LastAppliedVersion(url);
            var pending = LocalMigrations().Where(m => m.Version > applied).OrderBy(m => m.Version).ToList();

            if (pending.Count == 0)
            {
                Write(ConsoleColor.Yellow, "No migration is pending.");
            }

            foreach (var migration in pending)
            {
                Write(ConsoleColor.Yellow, $"    - Version: {migration.Version,-10} Name: {migration.Name}");
            }

            Write(ConsoleColor.Green, "Finished.");
        }));
    }

    public void ListApplied(CommandLineApplication cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        cmd.HelpOption("-? | -h | --help");
        cmd.Description = "Show the migrations recorded as applied to the database.";
        cmd.OnExecute(() => Run(() =>
        {
            var url = DatabaseUrl();
            Write(ConsoleColor.Red, $"List the migration(s) applied to database {url.DatabaseName}.");

            var applied = AppliedMigrations(url).OrderBy(m => m.ApplyingDateTime).ToList();

            if (applied.Count == 0)
            {
                Write(ConsoleColor.Yellow, "No migration has been applied.");
            }

            foreach (var migration in applied)
            {
                var direction = migration.isUp ? "Up" : "Down";
                Write(ConsoleColor.Yellow, $"    - Version: {migration.Ver,-10} Name: {migration.Name,-32} Direction: {direction,-4} Applied: {migration.ApplyingDateTime}");
            }

            Write(ConsoleColor.Green, "Finished.");
        }));
    }

    public void ListAll(CommandLineApplication cmd)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        cmd.HelpOption("-? | -h | --help");
        cmd.Description = "Show every migration defined in this project, applied or not.";
        cmd.OnExecute(() => Run(() =>
        {
            var url = DatabaseUrl();
            Write(ConsoleColor.Red, $"List the migration(s) defined for database {url.DatabaseName}.");

            var applied = LastAppliedVersion(url);

            foreach (var migration in LocalMigrations().OrderBy(m => m.Version))
            {
                var status = migration.Version > applied ? "Pending" : "Applied";
                Write(ConsoleColor.Yellow, $"    - Version: {migration.Version,-10} Name: {migration.Name,-32} Status: {status}");
            }

            Write(ConsoleColor.Green, "Finished.");
        }));
    }

    /// <summary>
    /// Run the engine up to <paramref name="targetVersion"/>, or to the newest migration in this assembly when
    /// it is <c>null</c>. Scheme validation is off: it compares the collections against the entities of a
    /// mapped assembly, which this tool deliberately does not reference.
    /// </summary>
    private void Migrate(string? targetVersion)
    {
        var url = DatabaseUrl();

        // Read the state first: everything below announces what is about to happen, and a header printed
        // before the database was even reached reads as progress on a run that never started.
        var applied = LastAppliedVersion(url);
        var pending = LocalMigrations().Where(m => m.Version > applied).OrderBy(m => m.Version).ToList();

        Write(ConsoleColor.Red, $"Migrate the database {url.DatabaseName}"
            + (string.IsNullOrEmpty(targetVersion) ? "." : $" up to version {targetVersion}."));

        if (pending.Count == 0)
        {
            Write(ConsoleColor.Yellow, "No migration is pending.");
            return;
        }

        Write(ConsoleColor.Yellow, "Pending migration(s) that will be applied:");

        foreach (var migration in pending)
        {
            Write(ConsoleColor.Yellow, $"    - Version: {migration.Version,-10} Name: {migration.Name}");
        }

        var engine = new MigrationEngine()
            .UseDatabase(url.ToString(), url.DatabaseName)
            .UseAssembly(Assembly.GetExecutingAssembly())
            .UseSchemeValidation(false)
            .UseCustomSpecificationCollectionName(HistoryCollection)
            .UseProgressHandler(Report);

        if (string.IsNullOrEmpty(targetVersion))
        {
            engine.Run();
        }
        else
        {
            engine.Run(targetVersion);
        }

        Write(ConsoleColor.Green, "Migration is finished.");
    }

    private static void Report(InterimMigrationResult step)
    {
        Console.WriteLine($"Executing migration {step.TargetVersion}: {step.MigrationName}. {step.CurrentNumber}/{step.TotalCount}");
    }

    /// <summary>
    /// The connection URI of the target database. MongoDB carries the database in the path of the URI, and
    /// every command below needs it by name, so a connection string without one is a configuration error
    /// rather than a default.
    /// </summary>
    private MongoUrl DatabaseUrl()
    {
        var connectionString = configuration.GetConnectionString("CampusHub_Billing");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"No connection string 'ConnectionStrings:CampusHub_Billing' for environment {Program.EnvironmentName}. "
                + "Development reads appsettings.json, any other environment Configs/appsettings.<env>.json.");
        }

        var url = new MongoUrl(connectionString);

        return string.IsNullOrEmpty(url.DatabaseName)
            ? throw new InvalidOperationException(
                $"The connection string 'ConnectionStrings:CampusHub_Billing' names no database. "
                + "Expected an URI of the form mongodb://<host>:<port>/CampusHub_Billing.")
            : url;
    }

    /// <summary>The migrations defined in this assembly, whatever their state in the database.</summary>
    private static List<IMigration> LocalMigrations()
    {
        var migrations = Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => typeof(IMigration).IsAssignableFrom(type)
                        && !type.IsAbstract
                        && type.GetCustomAttribute<IgnoreMigrationAttribute>() is null)
            .Select(Activator.CreateInstance)
            .OfType<IMigration>()
            .ToList();

        return migrations.Count == 0
            ? throw new InvalidOperationException(
                "No migration found in this project. Add a class implementing IMigration under Migrations/ - see Add-Migration.txt.")
            : migrations;
    }

    /// <summary>What the target database records as applied, read straight from the history collection.</summary>
    private static List<SpecificationItem> AppliedMigrations(MongoUrl url)
    {
        _ = _versionSerializerRegistered;

        var database = new MongoClient(url).GetDatabase(url.DatabaseName);

        return database.GetCollection<SpecificationItem>(HistoryCollection)
            .Find(Builders<SpecificationItem>.Filter.Empty)
            .ToList();
    }

    /// <summary>
    /// The newest version the database has applied, or the default 0.0.0 when it has none - a database that
    /// has never been migrated is the normal first run, not an error.
    /// </summary>
    private static MongoDBMigrations.Version LastAppliedVersion(MongoUrl url)
    {
        var applied = AppliedMigrations(url).Where(m => m.isUp).ToList();

        return applied.Count == 0
            ? default
            : applied.Max(m => m.Ver);
    }

    private static bool RegisterVersionSerializer()
    {
        BsonSerializer.RegisterSerializer<MongoDBMigrations.Version>(new VerstionStructSerializer());
        return true;
    }

    /// <summary>
    /// The console envelope every command shares: it restores the foreground color whichever way the command
    /// leaves, and turns a failure into the message plus the non-zero exit code the caller scripts on.
    /// </summary>
    private static int Run(Action command)
    {
        var previousColor = Console.ForegroundColor;
        try
        {
            command();
            return 0;
        }
        // A bad connection string, an unreachable server, a migration that throws half way: whatever the
        // failure, the operator gets the message and an exit code rather than an unhandled stack trace.
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Write(ConsoleColor.Red, ex.Message);
            return -1;
        }
        finally
        {
            Console.ForegroundColor = previousColor;
        }
    }

    private static void Write(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
    }
}
