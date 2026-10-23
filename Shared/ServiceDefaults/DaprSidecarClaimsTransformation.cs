using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Dev4u.CampusHub.ServiceDefaults;

/// <summary>
/// Keeps the Dapr sidecar's identity away from the Arc4u claims transformation.
/// <para>
/// ASP.NET Core runs the registered <see cref="IClaimsTransformation"/> after every successful authentication, the
/// "Dapr" scheme included (the sidecar authenticated by its dapr-api-token). Arc4u's transformation caches the claims
/// of a user under a key taken from the identity (oid, sub...), which the sidecar does not have: it throws, the
/// subscriber answers 500 and Dapr retries the event until it is dead-lettered. The sidecar is not a user and needs
/// no Arc4u principal, so its identity is passed through unchanged; every other identity goes to Arc4u as before.
/// </para>
/// </summary>
internal sealed class DaprSidecarClaimsTransformation(IClaimsTransformation inner) : IClaimsTransformation
{
    /// <summary>Authentication type of the identity created by Dapr.AspNetCore's "Dapr" scheme.</summary>
    public const string DaprAuthenticationType = "Dapr";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.Identity?.AuthenticationType == DaprAuthenticationType
            ? Task.FromResult(principal)
            : inner.TransformAsync(principal);
    }
}

public static class DaprSidecarClaimsTransformationExtensions
{
    /// <summary>
    /// Wraps the registered claims transformation (Arc4u's) in <see cref="DaprSidecarClaimsTransformation"/>. Call it
    /// after the authentication setup of a service that receives Dapr pub/sub events.
    /// </summary>
    public static IServiceCollection ExcludeDaprSidecarFromClaimsTransformation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var registered = services.LastOrDefault(d => d.ServiceType == typeof(IClaimsTransformation))
                         ?? throw new InvalidOperationException("No IClaimsTransformation is registered: call this after the authentication setup.");
        services.Remove(registered);

        Func<IServiceProvider, IClaimsTransformation> inner = registered switch
        {
            { ImplementationType: { } type } => sp => (IClaimsTransformation)ActivatorUtilities.CreateInstance(sp, type),
            { ImplementationFactory: { } factory } => sp => (IClaimsTransformation)factory(sp),
            { ImplementationInstance: IClaimsTransformation instance } => _ => instance,
            _ => throw new InvalidOperationException($"Unsupported IClaimsTransformation registration: {registered}."),
        };

        services.Add(new ServiceDescriptor(typeof(IClaimsTransformation),
                                           sp => new DaprSidecarClaimsTransformation(inner(sp)),
                                           registered.Lifetime));
        return services;
    }
}
