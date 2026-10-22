using Arc4u.AspNetCore.Results;
using Arc4u.Results;
using Dev4u.CampusHub.Catalog.Domain;
using Dev4u.CampusHub.Catalog.UseCases;
using Dev4u.CampusHub.Rights;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Catalog.FrontendApi;

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
    }



    private static Microsoft.AspNetCore.Authentication.AuthenticationProperties GetAuthProperties(string? returnUrl)
    {
        // Replicates the minimal API logic.
        return new Microsoft.AspNetCore.Authentication.AuthenticationProperties
        {
            RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
        };
    }
}