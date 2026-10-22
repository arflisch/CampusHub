using System.Security.Claims;
using Arc4u.AspNetCore.Results;
using Dev4u.CampusHub.Enrollment.UseCases.Enrollments;
using Dev4u.CampusHub.Rights;
using Dev4u.CampusHub.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Enrollment.FrontendApi;

public static partial class ApiDefinitions
{
    /// <summary>
    /// Registers the enrollment endpoints: a student requests an enrollment (202 Accepted, the saga runs in the
    /// background) and polls it until it is Confirmed or Rejected.
    /// </summary>
    public static void RegisterEnrollmentsApis(this WebApplication app)
    {
        var enrollments = app.MapGroup("frontend/enrollments")
                             .WithGroupName("frontend")
                             .WithTags("Enrollments")
                             .RequireAuthorization(nameof(Access.AccessApplication));

        enrollments.MapPost("", async Task<Results<Accepted<EnrollmentDto>, ProblemHttpResult>> (
                [FromBody] RequestEnrollmentCommand command, ClaimsPrincipal user, [FromServices] RequestEnrollment useCase,
                CancellationToken cancellationToken) =>
            {
                if (user.GetUserId() is not { } studentId)
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The token carries no user identifier.");
                }

                var result = await useCase.ExecuteAsync(command, studentId, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? TypedResults.Accepted($"/enrollment/frontend/enrollments/{result.Value.Id}", result.Value)
                    : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("Enrollments_Request")
            .WithSummary("Ask to join a course. Idempotent on requestId; poll the returned location for the outcome.");

        enrollments.MapGet("", async Task<Results<Ok<IReadOnlyList<EnrollmentDto>>, ProblemHttpResult>> (
                ClaimsPrincipal user, [FromServices] ListMyEnrollments useCase, CancellationToken cancellationToken) =>
            {
                var result = await useCase.ExecuteAsync(user.GetUserId() ?? string.Empty, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("Enrollments_ListMine")
            .WithSummary("My latest enrollments.");

        enrollments.MapGet("{enrollmentId:guid}", async Task<Results<Ok<EnrollmentDto>, ProblemHttpResult>> (
                Guid enrollmentId, ClaimsPrincipal user, [FromServices] GetEnrollment useCase, CancellationToken cancellationToken) =>
            {
                var result = await useCase.ExecuteAsync(enrollmentId, user.GetUserId() ?? string.Empty, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("Enrollments_Get")
            .WithSummary("Get one of my enrollments.");
    }
}
