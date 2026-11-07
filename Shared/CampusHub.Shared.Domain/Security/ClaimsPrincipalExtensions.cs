using System.Security.Claims;

namespace Dev4u.CampusHub.Security;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Stable identifier of the caller, as issued by the identity provider (<c>sub</c>). Identity always comes
    /// from the token, never from the request body.
    /// </summary>
    public static string? GetUserId(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}

/// <summary>Headers the gateway sets on the requests it forwards.</summary>
public static class GatewayHeaders
{
    /// <summary>
    /// Display name of the signed-in user, taken from the session by the gateway (the access token carries no name).
    /// Any value sent by a client is replaced. Display only: identity and rights always come from the token.
    /// </summary>
    public const string UserName = "X-CampusHub-User-Name";
}
