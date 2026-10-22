using Arc4u.Configuration;
using Arc4u.Dependency.Attribute;
using Arc4u.gRPC.Interceptors;
using Arc4u.OAuth2;
using Microsoft.Extensions.Options;

namespace Dev4u.CampusHub.Notification.Host.Infrastructure;

[Export]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated when gRPC client is defined in Program.cs.")]
    internal sealed class OAuth2GrpcInterceptor(IServiceProvider serviceProvider, ILogger<OAuth2GrpcInterceptor> logger)
    : OAuth2Interceptor<OAuth2GrpcInterceptor>(serviceProvider, logger, "OAuth2");
