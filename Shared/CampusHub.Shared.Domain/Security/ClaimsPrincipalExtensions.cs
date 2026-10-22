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
