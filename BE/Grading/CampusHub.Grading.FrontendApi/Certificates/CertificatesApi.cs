using System.Security.Claims;
using Arc4u.AspNetCore.Results;
using Dev4u.CampusHub.Grading.UseCases.Certificates;
using Dev4u.CampusHub.Rights;
using Dev4u.CampusHub.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Grading.FrontendApi;

public static partial class ApiDefinitions
{
    /// <summary>Registers the certificate endpoints used by the UIs: the signed-in student's certificates.</summary>
    public static void RegisterCertificateApis(this WebApplication app)
    {
        app.MapGroup("frontend/certificates")
           .WithGroupName("frontend")
           .WithTags("Certificates")
           .RequireAuthorization(nameof(Access.AccessApplication))
           .MapGet("", async Task<Results<Ok<IReadOnlyList<CertificateDto>>, ProblemHttpResult>> (
                ClaimsPrincipal user, [FromServices] ListMyCertificates useCase, CancellationToken cancellationToken) =>
            {
                var result = await useCase.ExecuteAsync(user.GetUserId() ?? string.Empty, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.ToProblemDetails());
            })
           .WithName("Certificates_ListMine")
           .WithSummary("My certificates, latest first.");
    }
}
