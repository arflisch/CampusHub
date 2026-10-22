using Arc4u.AspNetCore.Results;
using Arc4u.Results;
using Dev4u.CampusHub.Gtw.Domain;
using Dev4u.CampusHub.Gtw.UseCases;
using Dev4u.CampusHub.Rights;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Gtw.FrontendApi;

/// <summary>
/// Provides extension methods that register the frontend API's minimal API endpoints.
/// </summary>
public static partial class ApiDefinitions
{
    /// <summary>
    /// Registers the Environment API endpoints on the specified <see cref="WebApplication"/>.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication"/> to register the Environment endpoints on.</param>
    public static void RegisterEnvironmentApis(this WebApplication app)
    {
        var envGroup = app.MapGroup("frontend/Environment")
                          .WithGroupName("frontend")
                          .WithTags("Environment")
                          .RequireAuthorization();


        envGroup.MapGet("", async ([FromServices] GetEnvironmentInfo useCase, CancellationToken cancellationToken) =>
        {
            var res = await useCase.GetEnvironmentInfoAsync(cancellationToken).ConfigureAwait(false);

            Results<Ok<EnvironmentInfo>, ProblemHttpResult> objectResult = TypedResults.Problem();
            res
                .OnSuccessNotNull(value => objectResult = TypedResults.Ok(value))
                .OnSuccessNull(() => objectResult = TypedResults.Ok(default(EnvironmentInfo)))
                .OnFailed(errors => objectResult = TypedResults.Problem(res.ToProblemDetails()));

            return objectResult;
       })
          .WithName("Environment_Get")
          .RequireAuthorization(nameof(Access.AccessApplication))
          .WithSummary("Get environment information.")
          .WithDescription("Return generic information on the service like which server is running the service.")
          .Produces<EnvironmentInfo>(StatusCodes.Status200OK);

        envGroup.MapGet("logout", (HttpContext httpContext, [FromQuery] string? returnUrl) =>
            {
                var authProperties = GetAuthProperties(returnUrl);

                return TypedResults.SignOut(authProperties,
                [CookieAuthenticationDefaults.AuthenticationScheme,
                                    OpenIdConnectDefaults.AuthenticationScheme]);
            })
            .WithName("Environment_Logout")
            .WithSummary("Sign out the current user.")
            .ExcludeFromDescription();

        // BFF sign-in used by the Blazor front when a call returns 401: runs the OpenID Connect flow, then comes back.
        app.MapGet("frontend/Environment/login", ([FromQuery] string? returnUrl) =>
                TypedResults.Challenge(GetAuthProperties(returnUrl), [OpenIdConnectDefaults.AuthenticationScheme]))
           .WithName("Environment_Login")
           .AllowAnonymous()
           .ExcludeFromDescription();

        // Who is signed in, for the front's header. The token itself never reaches the browser.
        app.MapGet("frontend/me", (HttpContext httpContext) =>
            {
                var user = httpContext.User;
                var userId = user.FindFirst("sub")?.Value ?? string.Empty;
                var displayName = user.FindFirst("name")?.Value ?? user.FindFirst("preferred_username")?.Value ?? userId;
                return TypedResults.Ok(new CurrentUserInfo(userId, displayName));
            })
           .WithGroupName("frontend")
           .WithTags("Environment")
           .WithName("Me_Get")
           .RequireAuthorization(nameof(Access.AccessApplication));
    }




    private static Microsoft.AspNetCore.Authentication.AuthenticationProperties GetAuthProperties(string? returnUrl)
    {
        // Replicates the minimal API logic.
        return new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            // Local paths only: an absolute or protocol-relative url would make the gateway an open redirector.
            RedirectUri = IsLocalUrl(returnUrl) ? returnUrl : "/"
        };
    }

    private static bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}

/// <summary>The signed-in user as shown by the front.</summary>
public sealed record CurrentUserInfo(string UserId, string DisplayName);
