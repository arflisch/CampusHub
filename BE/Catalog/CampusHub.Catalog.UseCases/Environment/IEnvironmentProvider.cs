namespace Dev4u.CampusHub.Catalog.UseCases;

/// <summary>
/// Port giving the use cases access to the hosting environment without depending on
/// configuration or operating-system APIs. Implemented by the host.
/// </summary>
public interface IEnvironmentProvider
{
    /// <summary>
    /// Name of the environment the service runs in (Development, Staging, Production, ...).
    /// </summary>
    string EnvironmentName { get; }

    /// <summary>
    /// Name of the server running the service.
    /// </summary>
    string ServerName { get; }
}
