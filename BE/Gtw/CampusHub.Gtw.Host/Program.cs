using System.Globalization;
using System.Text.Json.Serialization;
using Arc4u.AspNetCore.Middleware;
using Arc4u.AspNetCore.Results;
using Arc4u.Caching;
using Arc4u.Configuration;
using Arc4u.Configuration.Decryptor;
using Arc4u.Dependency;
using Arc4u.Dependency.Attribute;
using Arc4u.Diagnostics;
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
using Dev4u.CampusHub.Gtw.Domain;
using Dev4u.CampusHub.Gtw.Host;
using Dev4u.CampusHub.Gtw.FrontendApi;
using Dev4u.CampusHub.Gtw.Host.Infrastructure;
using Dev4u.CampusHub.ServiceDefaults;
using FluentResults;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.OpenApi;
using Serilog;



Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

Log.Information("Starting up CampusHub.Gateway");

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.ClearProviders();
    // Remove sensitive information that can be exploited by hackers.
    builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

    // Add configuration sources
    builder.Configuration.AddJsonFile("configs/appsettings.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddJsonFile($"configs/appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddJsonFile("configs/reverseproxy.json", optional: true, reloadOnChange: true);
    builder.Configuration.AddJsonFile($"configs/reverseproxy.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    // Local secrets (ClientSecret, ...), kept out of git: see configs/appsettings.{Environment}.local.json.
    builder.Configuration.AddJsonFile($"configs/appsettings.{builder.Environment.EnvironmentName}.local.json", optional: true, reloadOnChange: true);
    // Environment variables and the command line (Aspire, containers, CI) must override the JSON files above:
    // they come first in WebApplication.CreateBuilder, so without this the local defaults would win.
    builder.Configuration.AddEnvironmentVariables();
    builder.Configuration.AddCommandLine(args);
    // The build-time OpenAPI generator boots the app with NSwag=true. Public Arc4u packages always load the
    // decryption certificate, so skip the decryptor there: the documents don't need any secret.
    if (!string.Equals(System.Environment.GetEnvironmentVariable("NSwag"), "true", StringComparison.OrdinalIgnoreCase))
    {
        builder.Configuration.AddCertificateDecryptorConfiguration(options => options.SecretSectionName = "Authentication:DataProtection:EncryptionCertificate");
    }

    // Configure the app to run as a Windows service
    builder.Host.UseWindowsService();

    builder.AddServiceDefaults();

    //Addservices to the container.
    var services = builder.Services;
    var configuration = builder.Configuration;

    services.RegisterHostTypes();
    services.RegisterUseCasesTypes();
    services.RegisterServiceDefaultsTypes();
    services.RegisterTypes();
    services.AddTransient<IObjectSerialization, JsonSerialization>();

    services.AddApplicationConfig(configuration);
    services.AddHttpContextLoggingProperties();
    services.AddApplicationContext();
    services.AddResultLogger();

    services.AddCustomRootCA(configuration);

    // Reverse proxy configuration + ServiceDiscovery.
    services.AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"))
            .AddServiceDiscoveryDestinationResolver();


    services.AddDefaultAuthorizationPolicies();
     // Add your custom policies here by chaining AddPolicy(...) with a ScopedOperationsRequirement built from Access values.
    #pragma warning disable S125 // Kept as an example.
    //.AddPolicy("Custom", policy =>
    //policy.Requirements.Add(new ScopedOperationsRequirement((int)Access.AccessApplication, (int)Access.CanSeeSwaggerFrontendApi)));
    #pragma warning restore S125

    // Minimal Apis.
    services.AddEndpointsApiExplorer();

    services.AddCacheContext(configuration);

    // OIDC needs the data-protection certificate of the company environment; the build-time OpenAPI generator
    // (NSwag=true) does not authenticate anybody, so it skips it.
    if (!string.Equals(System.Environment.GetEnvironmentVariable("NSwag"), "true", StringComparison.OrdinalIgnoreCase))
    {
        services.AddHybridAuthentication(configuration);
        services.AddForceOpenId(configuration);
    }
    else
    {
        // Without OIDC the token providers listed in the configuration cannot be built: don't validate them for
        // a host that is only started to read its OpenAPI documents.
        builder.Host.UseDefaultServiceProvider(options => options.ValidateOnBuild = false);
    }

    // Only the origins listed in Cors:AllowedOrigins may call the API cross-origin; none are allowed by default.
    var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod()));

    services.AddControllers(opt =>
    {
        // disable null to 204 => which will throw an exception.
        opt.OutputFormatters.RemoveType<HttpNoContentOutputFormatter>();
        opt.Filters.Add<ManageExceptionsFilter>();
        opt.Filters.Add<SetCultureActionFilter>();
    });
    services.AddManageExceptionsHandler();
    
    services
        .AddHealthChecksUI(setup =>
        {
            setup.SetHeaderText("CampusHub");
        })
        .AddInMemoryStorage()
        .Services
        .AddHealthChecks();

   services.AddOpenApi("frontend", options =>
    {
        options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
        options.ShouldInclude = description => description.GroupName == "frontend";
        options.AddDocumentTransformer<ProblemDetailsDefaultResponseTransformer>();
        options.AddDocumentTransformer((document, context, cancellationToken) =>
        {
            document.Info.Title = "Frontend contracts are only used by Guis and no sustainability is provided.";
            return Task.CompletedTask;
        });
    });

    services.AddOpenApi("interface", options =>
    {
        options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
        options.ShouldInclude = description => description.GroupName is "interface" or "deprecated";
        options.AddDocumentTransformer<ProblemDetailsDefaultResponseTransformer>();
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

    services.AddProblemDetails();

    var app = builder.Build();

    app.UseExceptionHandler(exceptionHandlerApp
    =>
    {
        exceptionHandlerApp.Run(async context
          =>
        {
            await Results.Problem(Result.Ok().ToGenericMessage())
                         .ExecuteAsync(context)
                         .ConfigureAwait(false);
        });
    });
    app.UseStatusCodePages();
    app.UseSecurityHeaderCSP(configuration);

    app.UseRouting();

    // The Blazor WebAssembly front (FE/CampusHub.Web) is served by the gateway, which is also its BFF.
    app.UseBlazorFrameworkFiles();
    app.UseStaticFiles();

    app.UseCors();
    app.UseCsrfProtection();

    app.AddGrpcAuthenticationControl();

    app.UseAuthentication();
    app.UseForceOpenId();
    app.UseAuthorization();
    app.UseAddContextToPrincipal();
    app.UseResourcesRightValidationFor();
    app.UseOpenIdBearerInjector();

    var jsVersion = Guid.NewGuid().ToString();

    // Serve the built-in OpenAPI documents at /openapi/frontend.json and /openapi/interface.json.
    app.MapOpenApi("/openapi/{documentName}.json");

    // NSwag serves the Swagger UI pages, pointed at the built-in documents above.
    app.UseSwaggerUi(configure =>
    {
        configure.CustomJavaScriptPath = $"/swagger.logout.js?v={jsVersion}";
        configure.Path = "/swagger/frontend";
        configure.DocumentPath = "/openapi/frontend.json";
        configure.TagsSorter = "alpha";
        configure.OperationsSorter = "alpha";
        configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Gateway", "/openapi/frontend.json"));
        configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Catalog", "/catalog/openapi/frontend.json"));
        configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Enrollment", "/enrollment/openapi/frontend.json"));
        configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Billing", "/billing/openapi/frontend.json"));
        configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Grading", "/grading/openapi/frontend.json"));
        configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Notification", "/notification/openapi/frontend.json"));
    })
     .UseSwaggerUi(configure =>
     {
         configure.CustomJavaScriptPath = $"/swagger.logout.js?v={jsVersion}";
         configure.Path = "/swagger/service";
         configure.DocumentPath = "/openapi/service.json";
         configure.TagsSorter = "alpha";
         configure.OperationsSorter = "alpha";
         configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Catalog", "/catalog/openapi/service.json"));
         configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Billing", "/billing/openapi/service.json"));
         configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Notification", "/notification/openapi/service.json"));
         configure.SwaggerRoutes.Add(new NSwag.AspNetCore.SwaggerUiRoute("Grading", "/grading/openapi/service.json"));
     });

    app.RegisterEnvironmentApis();
    app.MapControllers();

    app.MapDefaultEndpoints();

    app.MapHealthChecksUI();
    app.MapReverseProxy();
    // Client-side routes of the front (/teaching, /enrollments, ...) are answered with its index.html.
    app.MapFallbackToFile("index.html");

    var logger = app.Services.GetRequiredService<ILogger<Program>>();

    app.Services.InitializeTimeZoneContext();

    logger.Technical().LogStartApiProcess();

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

namespace Dev4u.CampusHub.Gtw.Host
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
