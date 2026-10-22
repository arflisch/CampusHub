using Arc4u.Configuration;
using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Billing.UseCases;
using Microsoft.Extensions.Options;

namespace Dev4u.CampusHub.Billing.Host.Infrastructure;

/// <summary>
/// Adapter for <see cref="IEnvironmentProvider"/> backed by the application configuration
/// and the machine name. Keeps configuration and OS concerns out of the use cases.
/// </summary>
[Export(typeof(IEnvironmentProvider)), Shared]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the DI container through the generated RegisterHostTypes().")]
internal sealed class EnvironmentProvider(IOptions<ApplicationConfig> config) : IEnvironmentProvider
{
    public string EnvironmentName => config.Value.Environment.Name;

    public string ServerName => System.Net.Dns.GetHostName();
}
