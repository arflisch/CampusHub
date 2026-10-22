using Arc4u.Dependency.Attribute;
using Arc4u.Extensions;
using Arc4u.IdentityModel.Claims;
using Arc4u.OAuth2.Token;
using Arc4u.Security.Principal;
using Dev4u.CampusHub.Rights;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arc4u.Configuration;
using Arc4u.Diagnostics;
using Arc4u.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dev4u.CampusHub.ServiceDefaults;

/// <summary>
/// Builds the Arc4u authorization claim of the caller from its access token: every signed-in user gets the "User"
/// role, teachers and administrators are recognised by <see cref="CampusHubRoles"/> (token permissions or the
/// user ids configured in <see cref="RoleOptions"/>). An identity without token claims gets no operation at all.
/// </summary>
[Export(typeof(IClaimsFiller)), Scoped]
public sealed class OAuthFiller(
                            IServiceProvider container,
                            ILogger<OAuthFiller> logger,
                            JsonSerializerContext serializationCtx,
                            IOptionsMonitor<SimpleKeyValueSettings> settings,
                            IOptionsMonitor<RoleOptions> roleOptions)
                            : IClaimsFiller
{
    public async Task<IEnumerable<ClaimDto>> GetAsync(IIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var result = new List<ClaimDto>();
        var claims = new List<Claim>();

        if (identity is not ClaimsIdentity claimsIdentity)
        {
            return result;
        }

        if (claimsIdentity.BootstrapContext is null && settings.Get(identity.AuthenticationType).Values.Count == 0)
        {
            return result;
        }

        try
        {
            JwtSecurityToken? bearerToken = null;
            if (null != claimsIdentity.BootstrapContext)
            {
                bearerToken = new JwtSecurityToken(claimsIdentity.BootstrapContext.ToString());
            }
            else
            {
                // find the Provider for the AuthenticationType!
                // exist because tested in the constructor!
                var providerSettings = settings.Get(identity.AuthenticationType).Values;

                var provider = container.GetRequiredKeyedService<ITokenProvider>(providerSettings[TokenKeys.ProviderIdKey]);

                await provider.GetTokenAsync(new SimpleKeyValueSettings(providerSettings), identity)
                    .OnSuccessNotNull(tokenInfo =>
                    {
                        bearerToken = new JwtSecurityToken(tokenInfo.Token);
                    }).ConfigureAwait(false);
            }

            if (null != bearerToken)
            {
                claims.AddRange(bearerToken.Claims);
            }
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.Technical().LogException(ex);
        }

        result.Add(BuildAuthorizationClaim(CampusHubRoles.RolesOf(claims, roleOptions.CurrentValue)));
        if (claims.Count == 0)
        {
            return result;
        }

        // Create a S-1-9 based on the object id.
        if (Guid.TryParse(ExtractClaimValue(claims, "http://schemas.microsoft.com/identity/claims/objectidentifier", "oid"), out var objectId))
        {
            result.Add(new ClaimDto { ClaimType = Arc4u.IdentityModel.Claims.ClaimTypes.Sid, Value = objectId.ToS19() });
        }
        result.Add(new ClaimDto { ClaimType = System.Security.Claims.ClaimTypes.Surname, Value = ExtractClaimValue(claims, "family_name") });
        result.Add(new ClaimDto { ClaimType = System.Security.Claims.ClaimTypes.GivenName, Value = ExtractClaimValue(claims, "given_name") });
        result.Add(new ClaimDto { ClaimType = System.Security.Claims.ClaimTypes.Email, Value = ExtractClaimValue(claims, "upn") });
        // These URIs are claim type identifiers, never dereferenced, so the http scheme is not a transport concern.
#pragma warning disable S5332
        result.Add(new ClaimDto { ClaimType = "http://schemas.arc4u.net/ws/2012/05/identity/claims/culture", Value = "en-GB" });
        result.Add(new ClaimDto { ClaimType = "http://schemas.arc4u.net/ws/2012/05/identity/claims/company", Value = "Arc4u" });
#pragma warning restore S5332

        return result;
    }

    private ClaimDto BuildAuthorizationClaim(IReadOnlyList<string> roles)
    {
        var allOperations = Enum.GetValues<Access>().Select(o => new Operation { Name = o.GetValue(), ID = (int)o }).ToList();
        var authorization = new Authorization
        {
            Operations = [new ScopedOperations { Operations = CampusHubRoles.OperationsOf(roles).Cast<int>().ToList(), Scope = "" }],
            AllOperations = allOperations,
            Roles = [new ScopedRoles { Roles = [.. roles], Scope = "" }],
            Scopes = []
        };

        var value = JsonSerializer.Serialize(authorization, typeof(Authorization), serializationCtx);
        return new ClaimDto { ClaimType = Arc4u.IdentityModel.Claims.ClaimTypes.Authorization, Value = value };
    }

    private static string ExtractClaimValue(IEnumerable<Claim> claims, params string[] claimTypes)
    {
        var claim = claims.FirstOrDefault(c => claimTypes.Any(t => t.Equals(c.Type, StringComparison.OrdinalIgnoreCase)));

        return claim is not null ? claim.Value : string.Empty;
    }
}
