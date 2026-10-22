using Arc4u.Configuration;
using Arc4u.Dependency.Attribute;
using Arc4u.OAuth2.Token;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dev4u.CampusHub.Enrollment.Host.Infrastructure;

/// <summary>
/// Puts the service-account bearer token on every REST call this host makes to another internal micro-service.
/// <para>
/// The token comes from the "Service" client token section of the environment appsettings - which is what the
/// class is named after, so a host impersonating several client tokens gets one handler per token - and the outgoing
/// call is therefore authenticated as the service account rather than as the caller of the incoming request. One
/// handler serves every internal REST client declared in Program.cs with that token: they all authenticate the same
/// way. Its gRPC counterpart is ServiceOAuth2GrpcInterceptor.
/// </para>
/// </summary>
[Export]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated when an http client is declared in Program.cs.")]
internal sealed class ServiceOAuth2HttpHandler(IServiceProvider container,
    ILogger<ServiceOAuth2HttpHandler> logger,
    IOptionsMonitor<SimpleKeyValueSettings> keyValuesSettingsOption)
    : JwtHttpHandler<ServiceOAuth2HttpHandler>(container, logger, keyValuesSettingsOption.Get("Service"));
