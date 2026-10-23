using Arc4u.AspNetCore.Results;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Dev4u.CampusHub.Billing.UseCases.Payments;
using Dev4u.CampusHub.Rights;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Billing.ServiceApi;

/// <summary>
/// Service-to-service contract used by the Enrollment saga to charge an enrollment and to refund it (compensation).
/// Both operations are idempotent on the enrollment id, so the saga's retries are safe.
/// </summary>
public static class PaymentsApi
{
    /// <summary>Registers the payment endpoints (version 1) of the service API.</summary>
    /// <param name="app">The application to register the endpoints on.</param>
    public static void RegisterPaymentServiceApis(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        ApiVersionSet versions = app.NewApiVersionSet("Payments")
                                    .HasApiVersion(ApiVersions.V1)
                                    .ReportApiVersions()
                                    .Build();

        var payments = app.MapGroup("service/payments")
                          .WithApiVersionSet(versions)
                          .MapToApiVersion(ApiVersions.V1)
                          .WithGroupName("service")
                          .WithTags("Payments")
                          .RequireAuthorization(nameof(Access.AccessApplication));

        payments.MapPost("", async Task<Results<Ok<PaymentDto>, ProblemHttpResult>> (
                [FromBody] ChargeEnrollmentCommand command, [FromServices] ChargeEnrollment useCase, CancellationToken cancellationToken) =>
            {
                var result = await useCase.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("Payments_Charge")
            .WithSummary("Charge a student for an enrollment (idempotent on the enrollment id; 422 payment.declined when the card is refused).");

        payments.MapPost("{enrollmentId:guid}/refund", async Task<Results<NoContent, ProblemHttpResult>> (
                Guid enrollmentId, [FromServices] RefundEnrollment useCase, CancellationToken cancellationToken) =>
            {
                var result = await useCase.ExecuteAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("Payments_Refund")
            .WithSummary("Give the money of an enrollment back (saga compensation; nothing to refund is a success).");
    }
}
