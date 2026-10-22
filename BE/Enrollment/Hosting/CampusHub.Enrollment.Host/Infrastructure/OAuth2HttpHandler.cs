using Arc4u.Configuration;
using Arc4u.Dependency.Attribute;
using Arc4u.OAuth2;
using Arc4u.OAuth2.Token;
using Microsoft.Extensions.Options;

namespace Dev4u.CampusHub.Enrollment.Host.Infrastructure;

[Export]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated when http client is defined in Program.cs.")]
internal sealed class OAuth2HttpHandler(IServiceProvider container,
                                        ILogger<OAuth2HttpHandler> logger)
                        : JwtHttpHandler<OAuth2HttpHandler>(container, logger, "OAuth2");