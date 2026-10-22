using Arc4u.AspNetCore.Versioning;
using Asp.Versioning;

namespace Dev4u.CampusHub.Catalog.ServiceApi;

/// <summary>
/// Defines the available versions of the service API.
/// </summary>
public static class ApiVersions
{
    /// <summary>
    /// The first version of the service API.
    /// </summary>
    public static ApiVersion V1 => ApiVersionDefault.V1;
}
