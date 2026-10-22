using System.Net;
using Microsoft.AspNetCore.Components;

namespace Dev4u.CampusHub.Web.Api;

/// <summary>
/// Outgoing calls to the gateway (same origin, session cookie sent by the browser):
/// <list type="bullet">
/// <item>adds the <c>X-CSRF</c> header the gateway requires on cookie-authenticated writes - a cross-site form cannot
/// add a custom header, so a forged request is refused;</item>
/// <item>on 401 (no or expired session), sends the browser to the gateway's sign-in endpoint, which runs the OpenID
/// Connect flow and comes back to the current page.</item>
/// </list>
/// </summary>
internal sealed class GatewayMessageHandler(NavigationManager navigation) : DelegatingHandler
{
    public const string CsrfHeader = "X-CSRF";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(CsrfHeader, "1");
        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var returnUrl = new Uri(navigation.Uri).PathAndQuery;
            navigation.NavigateTo($"frontend/Environment/login?returnUrl={Uri.EscapeDataString(returnUrl)}", forceLoad: true);
        }

        return response;
    }
}
