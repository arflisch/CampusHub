using System.Globalization;
using System.Text.Json.Serialization;
using Arc4u;
using Arc4u.AspNetCore.Middleware;
using Arc4u.AspNetCore.Results;
using Arc4u.AspNetCore.Versioning;
using Arc4u.Caching;
using Arc4u.Configuration;
using Arc4u.Configuration.Decryptor;
using Arc4u.Dependency;
using Arc4u.Dependency.Attribute;
using Arc4u.Diagnostics;
using Arc4u.Dispatcher;
using Arc4u.Dispatcher.Notification;
using Arc4u.gRPC.Interceptors;
using Arc4u.IdentityModel.Claims;
using Arc4u.OAuth2.AspNetCore.Diagnostics;
using Arc4u.OAuth2.AspNetCore.Filters;
using Arc4u.OAuth2.Extensions;
using Arc4u.OAuth2.Middleware;
using Arc4u.OAuth2.Token;
using Arc4u.Results.Logging;
using Arc4u.Security.CustomRootCA;
using Arc4u.Security.Principal;
using Arc4u.Serializer;
using Arc4u.MongoDB;
using Dev4u.CampusHub.Grading.Domain;
using Dev4u.CampusHub.Grading.Infrastructure.Grades;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Dev4u.CampusHub.Grading.FrontendApi;
using Dev4u.CampusHub.Grading.Host;
using Dev4u.CampusHub.Grading.Host.Infrastructure;
using Dev4u.CampusHub.Grading.Jobs;
using Dev4u.CampusHub.Rights;
using Dev4u.CampusHub.ServiceDefaults;
using FluentResults;
using Hangfire;
using Hangfire.Mongo;
using Hangfire.Mongo.Migration.Strategies;
using Hangfire.Mongo.Migration.Strategies.Backup;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Serilog;
using MongoDB.Driver;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

Log.Information("Starting up CampusHub.Grading");

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();
    // Remove sensitive information that can be exploited by hackers.
    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

    // Add configuration sources
    builder.Configuration.AddJsonFile("Configs/appsettings.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddJsonFile($"Configs/appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    // Local secrets (ClientSecret, ...), kept out of git: see Configs/appsettings.{Environment}.local.json.
    builder.Configuration.AddJsonFile($"Configs/appsettings.{builder.Environment.EnvironmentName}.local.json", optional: true, reloadOnChange: true);
    // Environment variables and the command line (Aspire, containers, CI) must override the JSON files above:
    // they come first in WebApplication.CreateBuilder, so without this the local defaults would win.
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
    // The build-time OpenAPI generator boots the app with NSwag=true. Public Arc4u packages always load the
    // decryption certificate, so skip the decryptor there: the documents don't need any secret.
    if (!string.Equals(System.Environment.GetEnvironmentVariable("NSwag"), "true", StringComparison.OrdinalIgnoreCase))
    {
        builder.Configuration.AddCertificateDecryptorConfiguration();
    }

    // Configure the app to run as a Windows service
    builder.Host.UseWindowsService();

    builder.AddServiceDefaults();

    var services = builder.Services;

    var configuration = builder.Configuration;

    services.RegisterHostTypes();
    services.RegisterUseCasesTypes();
    services.RegisterGradesTypes();
    services.RegisterServiceDefaultsTypes();
    services.RegisterTypes();
    services.AddTransient<IObjectSerialization, JsonSerialization>();

    services.AddApplicationConfig(configuration);
    services.AddHttpContextLoggingProperties();    
    services.AddApplicationContext();
    if (!IsInvokedByNSwag())
    {
        services.AddHangfire(hangfireConfig => hangfireConfig
	    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
	    .UseSimpleAssemblyNameTypeSerializer()
	    .UseRecommendedSerializerSettings()
	    .UseMongoStorage(configuration.GetConnectionString("HangfireConnection"), new MongoStorageOptions
        {
            MigrationOptions = new MongoMigrationOptions
            {
                MigrationStrategy = new MigrateMongoMigrationStrategy(),
                BackupStrategy = new CollectionMongoBackupStrategy()
            },
		    Prefix = "hangfire",
		    CheckConnection = true,
            // MongoDB runs as a replica set: use change streams.
            CheckQueuedJobsStrategy = CheckQueuedJobsStrategy.Watch
        }));
        services.AddHangfireServer();
    }
    // Arc4u copies most settings from the connection string but not directConnection, which a single-node
    // replica set (the local Aspire MongoDB) needs: re-apply it explicitly.
    var gradingMongoUrl = MongoUrl.Create(configuration.GetConnectionString("CampusHub_Grading"));
    services.AddMongoDatabase<Dev4u.CampusHub.Grading.Infrastructure.Grades.DatabaseContext>(configuration, "CampusHub_Grading",
        settings => settings.DirectConnection = gradingMongoUrl.DirectConnection);
    // In the background: the host must not wait for MongoDB to start.
    services.AddRetryingStartupTask("grading-mongodb-indexes", (sp, ct) =>
        GradingIndexes.EnsureAsync(sp.GetRequiredService<IMongoClientFactory<Dev4u.CampusHub.Grading.Infrastructure.Grades.DatabaseContext>>(), ct));
    services.TryAddSingleton(TimeProvider.System);
    services.AddResultLogger();

    services.AddCustomRootCA(configuration);

    services.AddDefaultAuthorizationPolicies();
    // Add your custom policies here by chaining AddPolicy(...) with a ScopedOperationsRequirement built from Access values.
    #pragma warning disable S125 // Kept as an example.
    //.AddPolicy("Custom", policy =>
    //policy.Requirements.Add(new ScopedOperationsRequirement((int)Access.AccessApplication, (int)Access.CanSeeSwaggerFrontendApi)));
    #pragma warning restore S125

    // Minimal Apis.
    services.AddEndpointsApiExplorer();
    services.AddServiceApiVersioning();

    services.AddCacheContext(configuration);

    services.AddJwtAuthentication(configuration);

    // Subscriber endpoints are reserved to the Dapr sidecar: it sends APP_API_TOKEN in the dapr-api-token header,
    // checked by the "Dapr" policy (see DaprController).
    services.AddAuthentication().AddDapr();
    services.AddAuthorization(options => options.AddDapr());
    // The sidecar is not a user: keep its identity away from the Arc4u claims transformation (see ServiceDefaults).
    services.ExcludeDaprSidecarFromClaimsTransformation();

    services.AddNotificationHandlersAsScoped();

    // Only the origins listed in Cors:AllowedOrigins may call the API cross-origin; none are allowed by default.
    var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

    services.AddControllers(opt =>
    {
        // disable null to 204 => which will throw an exception.
        opt.OutputFormatters.RemoveType<HttpNoContentOutputFormatter>();
        opt.Filters.Add<ManageExceptionsFilter>();
        opt.Filters.Add<SetCultureActionFilter>();
    }).AddDapr();
    services.AddManageExceptionsHandler();
    
    services
        .AddHealthChecks();

    // OpenAPI documents are produced by the built-in Microsoft.AspNetCore.OpenApi generator
    // (.NET 10). NSwag is kept only to serve the Swagger UI page (see UseSwaggerUi below).
    // ShouldInclude replicates NSwag's ApiGroupNames filtering: a document named "interface"
    // would otherwise also pull in ungrouped endpoints, so the predicate is set explicitly.
    // The generated OpenAPI server address is derived from the incoming request. When the document is
    // fetched through the gateway (YARP), ForwardedServerDocumentTransformer rewrites it to the
    // externally reachable gateway address using the forwarded headers.
    services.AddHttpContextAccessor();

    services.AddOpenApi("frontend", options =>
    {
        options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
        options.ShouldInclude = description => description.GroupName == "frontend";
        options.AddDocumentTransformer<ProblemDetailsDefaultResponseTransformer>();
        options.AddDocumentTransformer<ForwardedServerDocumentTransformer>();
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "Frontend contracts are only used by Guis and no sustainability is provided.";
            return Task.CompletedTask;
        });
    });

    services.AddOpenApi("service", options =>
    {
        options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
        options.ShouldInclude = description => description.GroupName is "service" or "deprecated";
        options.AddDocumentTransformer<ProblemDetailsDefaultResponseTransformer>();
        options.AddDocumentTransformer<ForwardedServerDocumentTransformer>();
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "Interface description.";
            return Task.CompletedTask;
        });
    });

    services.AddGrpc(options =>
    {
        options.Interceptors.Add<AuthorizationInterceptor>();
        options.MaxSendMessageSize = 5 * 1024 * 1024; // 5 MB
        options.MaxReceiveMessageSize = 5 * 1024 * 1024; // 5 MB
    });

    services.AddGrpcReflection();
 
    var app = builder.Build();

    app.UseExceptionHandler();

    app.UseCloudEvents();

    app.UseRouting();

    app.UseCors();

    app.AddGrpcAuthenticationControl();

    app.UseAuthentication();

    app.UseResourcesRightValidationFor();
    app.UseAddContextToPrincipal();

    app.UseAuthorization();

    // Always replace the virtual path of this backend with the one from the yarp. Required for swagger and hangfire when hosted in IISHost!
    app.Use((context, next) =>
    {
        var pathBase = context.Request.Headers["X-Forwarded-Prefix"];
        if (!string.IsNullOrWhiteSpace(pathBase))
        {
            context.Request.PathBase = new PathString(pathBase);
        }

        return next();
    });

    // Serve the built-in OpenAPI documents at /openapi/frontend.json and /openapi/interface.json.
    app.MapOpenApi("/openapi/{documentName}.json");

    // NSwag serves the Swagger UI pages, pointed at the built-in documents above.
    app.UseSwaggerUi(configure =>
        {
            configure.Path = "/swagger/frontend";
            configure.DocumentPath = "/openapi/frontend.json";
            configure.TagsSorter = "alpha";
            configure.OperationsSorter = "alpha";
        })
        .UseSwaggerUi(configure =>
        {
            configure.Path = "/swagger/service";
            configure.DocumentPath = "/openapi/service.json";
            configure.TagsSorter = "alpha";
            configure.OperationsSorter = "alpha";
        });

    app.MapDefaultEndpoints();

    app.UseWelcomePage("/");

    app.RegisterEnvironmentApis();

    if (!IsInvokedByNSwag())
    {
        app.MapHangfireDashboardWithAuthorizationPolicy(nameof(Access.CanSeeJobs), "/grading/jobs", new DashboardOptions { AppPath = "/healthchecks-ui#/healthchecks" });
    }


    app.MapControllers();
    app.MapSubscribeHandler();
    app.MapGrpcReflectionService();

    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    app.Services.InitializeTimeZoneContext();

    logger.Technical().LogStartApiProcess();

    // Recurring jobs are registered before the host runs (the generated code did it after RunAsync returned,
    // i.e. only at shutdown, so the jobs were never scheduled).
    if (!IsInvokedByNSwag())
    {
        RegisterJobs.Register(app.Services.GetRequiredService<IRecurringJobManager>());
    }

    await app.RunAsync().ConfigureAwait(false);

}
// HostAbortedException is thrown by build-time tooling (dotnet-getdocument / EF design-time) after
// the host is built to stop it from actually running; it must propagate, not be swallowed here.
#pragma warning disable CA1031
catch (Exception ex) when (ex is not HostAbortedException)
#pragma warning restore CA1031
{
    Log.Fatal(ex, "Unhandled exception");
}
finally
{
    Log.Information("Shut down complete");
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

static bool IsInvokedByNSwag()
{
	var env = System.Environment.GetEnvironmentVariable("NSwag");
	return env is not null && bool.TryParse(env, out var nswag) && nswag;
}


namespace Dev4u.CampusHub.Grading.Host
{
    [Export(typeof(JsonSerializerContext)), Shared]
    [JsonSerializable(typeof(EnvironmentInfo))]
    [JsonSerializable(typeof(byte[]))]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(IEnumerable<ClaimDto>))]
    [JsonSerializable(typeof(List<ClaimDto>))]
    [JsonSerializable(typeof(Authorization))]
    [JsonSerializable(typeof(TokenInfo))]
    internal sealed partial class AppJsonSerializerContext : JsonSerializerContext
    {

    }
}
