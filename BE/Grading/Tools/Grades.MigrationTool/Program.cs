using Arc4u.Configuration.Decryptor;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Configuration;

namespace Dev4u.CampusHub.Grading.Infrastructure.Grades.MigrationTool;

/// <summary>
/// Migration tool for CampusHub.Grading.Infrastructure.Grades, over the CampusHub_Grading database.
/// <para>
/// MongoDB has no schema to migrate, but it still has state a release can depend on: the indexes a query
/// plan needs, and documents written by an older shape of an entity. That is what the migrations in
/// Migrations/ own, and this tool is what applies them - see Add-Migration.txt for how to add one.
/// </para>
/// <para>
/// The environment it targets comes from DOTNET_ENVIRONMENT and selects the connection string: Development
/// (the default) reads this project's own appsettings.json, every other environment
/// Configs/appsettings.&lt;env&gt;.json.
/// </para>
/// </summary>
static class Program
{
    private static int Main(string[] args)
    {
        var tool = new MigrationTool(Configuration());

        using var app = new CommandLineApplication
        {
            Name = "CampusHub.Grading.Infrastructure.Grades.MigrationTool",
            Description = "Applies the migrations of CampusHub.Grading.Infrastructure.Grades to the CampusHub_Grading database.",
        };

        app.Command("apply", tool.Apply);
        app.Command("apply-all", tool.ApplyAll);
        app.Command("list-pendings", tool.ListPending);
        app.Command("list-applied", tool.ListApplied);
        app.Command("list-all", tool.ListAll);

        app.OnExecute(() =>
        {
            Console.WriteLine("Specify a subcommand.");
            app.ShowHelp();
            return 1;
        });

        app.HelpOption("-? | -h | --help");

        try
        {
            return app.Execute(args);
        }
        // A misspelled command or option: the operator gets the message and the usage, not a stack trace.
        catch (CommandParsingException ex)
        {
            Console.WriteLine(ex.Message);
            app.ShowHelp();
            return 1;
        }
    }

    /// <summary>
    /// The environment whose connection string is used, from DOTNET_ENVIRONMENT. Development is the default,
    /// and its connection string is the one in this project's own appsettings.json; every other environment
    /// reads Configs/appsettings.&lt;env&gt;.json.
    /// </summary>
    public static string EnvironmentName =>
        System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";

    /// <summary>
    /// The configuration the tool reads its connection string from. The decryptor is the last source on
    /// purpose: it rewrites the "Decrypt:..." values the deployed environments carry, whichever file they
    /// came from, and leaves a plain development connection string alone.
    /// </summary>
    public static IConfiguration Configuration()
    {
        return new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", true, true)
            .AddJsonFile($"Configs/appsettings.{EnvironmentName}.json", true, true)
            .AddCertificateDecryptorConfiguration()
            .Build();
    }
}
