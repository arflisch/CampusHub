using Arc4u.AspNetCore.Results;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Dev4u.CampusHub.Catalog.UseCases.Courses;
using Dev4u.CampusHub.Rights;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Catalog.ServiceApi;

/// <summary>
/// Service-to-service contract used by the Enrollment saga to hold, confirm and give back seats. Every operation
/// is idempotent so the saga's retries are safe.
/// </summary>
public static class SeatReservationsApi
{
    /// <summary>Registers the seat reservation endpoints (version 1) of the service API.</summary>
    /// <param name="app">The application to register the endpoints on.</param>
    public static void RegisterSeatReservationApis(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        ApiVersionSet versions = app.NewApiVersionSet("SeatReservations")
                                    .HasApiVersion(ApiVersions.V1)
                                    .ReportApiVersions()
                                    .Build();

        var reservations = app.MapGroup("service/courses/{courseId:guid}/reservations")
                              .WithApiVersionSet(versions)
                              .MapToApiVersion(ApiVersions.V1)
                              .WithGroupName("service")
                              .WithTags("SeatReservations")
                              .RequireAuthorization(nameof(Access.AccessApplication));

        reservations.MapPost("", async Task<Results<Ok<SeatReservationDto>, ProblemHttpResult>> (
                Guid courseId, [FromBody] ReserveSeatCommand command, [FromServices] ReserveSeat useCase, CancellationToken cancellationToken) =>
            {
                var result = await useCase.ExecuteAsync(courseId, command, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("SeatReservations_Reserve")
            .WithSummary("Hold a seat for a student (idempotent on the idempotency key).");

        reservations.MapPost("{reservationId:guid}/confirm", async Task<Results<NoContent, ProblemHttpResult>> (
                Guid courseId, Guid reservationId, [FromServices] ConfirmSeat useCase, CancellationToken cancellationToken) =>
            {
                var result = await useCase.ExecuteAsync(courseId, reservationId, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("SeatReservations_Confirm")
            .WithSummary("Confirm a held seat.");

        reservations.MapPost("{reservationId:guid}/release", async Task<Results<NoContent, ProblemHttpResult>> (
                Guid courseId, Guid reservationId, [FromServices] ReleaseSeat useCase, CancellationToken cancellationToken) =>
            {
                var result = await useCase.ExecuteAsync(courseId, reservationId, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.NoContent() : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("SeatReservations_Release")
            .WithSummary("Give a seat back (saga compensation).");
    }
}
