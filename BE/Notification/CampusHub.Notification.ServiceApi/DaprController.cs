using Dev4u.CampusHub.Enrollment.IntegrationEvents;
using Dev4u.CampusHub.Errors;
using Dev4u.CampusHub.Notification.UseCases.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Notification.ServiceApi;

/// <summary>
/// Subscriber endpoints called by the Dapr sidecar (routes declared in Dapr/*/CampusHub-Notification-*-Subscription.yaml).
/// <para>
/// Only the sidecar may call them: the "Dapr" policy checks the dapr-api-token header against APP_API_TOKEN.
/// The response drives the redelivery: 200 = processed, {"status":"RETRY"} = transient failure (redelivered by the
/// resiliency policy, then dead-lettered), {"status":"DROP"} = permanent failure (logged, not redelivered).
/// Unexpected exceptions propagate and produce a 500, which Dapr also retries.
/// </para>
/// </summary>
[ApiController]
[Authorize(Policy = "Dapr")]
[ApiExplorerSettings(GroupName = "dapr")]
[Route("Dapr")]
public sealed class DaprController(ILogger<DaprController> logger) : ControllerBase
{
    /// <summary>A student's enrollment was confirmed: notify the student.</summary>
    /// <param name="integrationEvent">The CloudEvent data published by the Enrollment service.</param>
    /// <param name="useCase">The use case creating the notification.</param>
    /// <param name="cancellationToken">Signalled when the sidecar gives up on the call.</param>
    [HttpPost("Enrollment/Confirmed")]
    public async Task<IActionResult> EnrollmentConfirmed([FromBody] EnrollmentConfirmedV1 integrationEvent,
                                                         [FromServices] NotifyEnrollmentConfirmed useCase,
                                                         CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(useCase);

        var result = await useCase.ExecuteAsync(
            new NotifyEnrollmentConfirmedCommand(integrationEvent.EventId, integrationEvent.StudentId, integrationEvent.CourseTitle, integrationEvent.OccurredAt),
            cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            logger.LogEventProcessed(EnrollmentEvents.EnrollmentConfirmedV1Type, integrationEvent.EventId, duplicate: !result.Value);
            return Ok();
        }

        var code = result.Errors.FirstCode();
        if (result.Errors.AreTransient())
        {
            logger.LogEventRetried(EnrollmentEvents.EnrollmentConfirmedV1Type, integrationEvent.EventId, code);
            return Ok(new { status = "RETRY" });
        }

        logger.LogEventDropped(EnrollmentEvents.EnrollmentConfirmedV1Type, integrationEvent.EventId, code);
        return Ok(new { status = "DROP" });
    }
}
