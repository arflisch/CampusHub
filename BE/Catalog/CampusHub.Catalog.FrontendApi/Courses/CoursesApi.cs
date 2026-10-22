using System.Security.Claims;
using Arc4u.AspNetCore.Results;
using Dev4u.CampusHub.Catalog.UseCases.Courses;
using Dev4u.CampusHub.Rights;
using Dev4u.CampusHub.Security;
using FluentResults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Catalog.FrontendApi;

public static partial class ApiDefinitions
{
    /// <summary>
    /// Registers the course endpoints used by the UIs: browse the catalogue, create and publish a course.
    /// </summary>
    public static void RegisterCoursesApis(this WebApplication app)
    {
        var courses = app.MapGroup("frontend/courses")
                         .WithGroupName("frontend")
                         .WithTags("Courses")
                         .RequireAuthorization(nameof(Access.AccessApplication));

        courses.MapGet("", async ([FromServices] ListPublishedCourses useCase, [FromQuery] int? skip, [FromQuery] int? take,
                                  CancellationToken cancellationToken) =>
            ToOkOrProblem(await useCase.ExecuteAsync(skip ?? 0, take ?? 20, cancellationToken).ConfigureAwait(false)))
            .WithName("Courses_List")
            .WithSummary("List the courses open for enrollment.")
            .Produces<IReadOnlyList<CourseDto>>(StatusCodes.Status200OK);

        courses.MapGet("mine", async ([FromServices] ListMyCourses useCase, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            ToOkOrProblem(await useCase.ExecuteAsync(user.GetUserId() ?? string.Empty, cancellationToken).ConfigureAwait(false)))
            .WithName("Courses_ListMine")
            .WithSummary("List the courses I teach, drafts included.")
            .Produces<IReadOnlyList<CourseDto>>(StatusCodes.Status200OK);

        courses.MapGet("{courseId:guid}", async (Guid courseId, ClaimsPrincipal user, [FromServices] GetCourse useCase,
                                                  CancellationToken cancellationToken) =>
            ToOkOrProblem(await useCase.ExecuteAsync(courseId, user.GetUserId() ?? string.Empty, cancellationToken).ConfigureAwait(false)))
            .WithName("Courses_Get")
            .WithSummary("Get a course.")
            .Produces<CourseDto>(StatusCodes.Status200OK);

        courses.MapPost("", async Task<Results<Created<CourseDto>, ProblemHttpResult>> (
                [FromBody] CreateCourseCommand command, ClaimsPrincipal user, [FromServices] CreateCourse useCase,
                CancellationToken cancellationToken) =>
            {
                if (user.GetUserId() is not { } teacherId)
                {
                    return MissingUserId();
                }

                var result = await useCase.ExecuteAsync(command, teacherId, cancellationToken).ConfigureAwait(false);
                return result.IsSuccess
                    ? TypedResults.Created($"/catalog/frontend/courses/{result.Value.Id}", result.Value)
                    : TypedResults.Problem(result.ToProblemDetails());
            })
            .WithName("Courses_Create")
            .WithSummary("Create a course in Draft; the caller becomes its teacher.");

        courses.MapPost("{courseId:guid}/publish", async Task<Results<Ok<CourseDto>, ProblemHttpResult>> (
                Guid courseId, ClaimsPrincipal user, [FromServices] PublishCourse useCase, CancellationToken cancellationToken) =>
            {
                if (user.GetUserId() is not { } userId)
                {
                    return MissingUserId();
                }

                return ToOkOrProblem(await useCase.ExecuteAsync(courseId, userId, cancellationToken).ConfigureAwait(false));
            })
            .WithName("Courses_Publish")
            .WithSummary("Open a course for enrollment (its teacher only).");
    }

    private static Results<Ok<T>, ProblemHttpResult> ToOkOrProblem<T>(Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.ToProblemDetails());

    private static ProblemHttpResult MissingUserId() =>
        TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "The token carries no user identifier.");
}
