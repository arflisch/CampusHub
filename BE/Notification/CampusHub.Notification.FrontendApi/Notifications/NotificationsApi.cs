using System.Security.Claims;
using Arc4u.AspNetCore.Results;
using Dev4u.CampusHub.Notification.UseCases.Notifications;
using Dev4u.CampusHub.Rights;
using Dev4u.CampusHub.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Notification.FrontendApi;

public static partial class ApiDefinitions
{
    /// <summary>Registers the endpoints of the current user's notifications: list them, mark them as read.</summary>
    public static void RegisterNotificationsApis(this WebApplication app)
    {
        var notifications = app.MapGroup("frontend/notifications")
                               .WithGroupName("frontend")
                               .WithTags("Notifications")
                               .RequireAuthorization(nameof(Access.AccessApplication));

        notifications.MapPost("read", async Task<Results<NoContent, ProblemHttpResult>> (
               ClaimsPrincipal user, [FromServices] MarkMyNotificationsRead useCase, CancellationToken cancellationToken) =>
           {
               var result = await useCase.ExecuteAsync(user.GetUserId() ?? string.Empty, notificationId: null, cancellationToken).ConfigureAwait(false);
               return result.IsSuccess ? TypedResults.NoContent() : TypedResults.Problem(result.ToProblemDetails());
           })
           .WithName("Notifications_MarkAllRead")
           .WithSummary("Mark all my notifications as read.");

        notifications.MapPost("{notificationId:guid}/read", async Task<Results<NoContent, ProblemHttpResult>> (
               Guid notificationId, ClaimsPrincipal user, [FromServices] MarkMyNotificationsRead useCase, CancellationToken cancellationToken) =>
           {
               var result = await useCase.ExecuteAsync(user.GetUserId() ?? string.Empty, notificationId, cancellationToken).ConfigureAwait(false);
               return result.IsSuccess ? TypedResults.NoContent() : TypedResults.Problem(result.ToProblemDetails());
           })
           .WithName("Notifications_MarkRead")
           .WithSummary("Mark one of my notifications as read.");

        notifications.MapGet("", async Task<Results<Ok<IReadOnlyList<NotificationDto>>, ProblemHttpResult>> (
               ClaimsPrincipal user, [FromServices] ListMyNotifications useCase, CancellationToken cancellationToken) =>
           {
               // Always the caller's own notifications: the recipient comes from the token, never from the request.
               var result = await useCase.ExecuteAsync(user.GetUserId() ?? string.Empty, cancellationToken).ConfigureAwait(false);
               return result.IsSuccess ? TypedResults.Ok(result.Value) : TypedResults.Problem(result.ToProblemDetails());
           })
           .WithName("Notifications_ListMine")
           .WithSummary("My latest notifications.");
    }
}
