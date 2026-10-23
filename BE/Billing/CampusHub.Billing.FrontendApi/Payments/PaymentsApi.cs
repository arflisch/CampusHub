using System.Security.Claims;
using Arc4u.AspNetCore.Results;
using Dev4u.CampusHub.Billing.UseCases.Payments;
using Dev4u.CampusHub.Rights;
using Dev4u.CampusHub.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Billing.FrontendApi;

public static partial class ApiDefinitions
{
    /// <summary>Registers the payment endpoints used by the UIs: the signed-in student's payments.</summary>
    public static void RegisterPaymentApis(this WebApplication app)
    {
        var payments = app.MapGroup("frontend/payments")
                          .WithGroupName("frontend")
                          .WithTags("Payments")
                          .RequireAuthorization(nameof(Access.AccessApplication));

        payments.MapGet("", async Task<Results<Ok<IReadOnlyList<PaymentDto>>, ProblemHttpResult>> (
                ClaimsPrincipal user, [FromServices] ListMyPayments useCase, CancellationToken cancellationToken) =>
            {
                if (user.GetUserId() is not { } studentId)
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The token carries no user identifier.");
                }

                var result = await useCase.ExecuteAsync(studentId, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("Payments_ListMine")
            .WithSummary("List my payments, latest first.");
    }
}
