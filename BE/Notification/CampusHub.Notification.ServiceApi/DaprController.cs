using Dev4u.CampusHub.Enrollment.IntegrationEvents;
using Dev4u.CampusHub.Errors;
using Dev4u.CampusHub.Grading.IntegrationEvents;
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
        return Acknowledge(EnrollmentEvents.EnrollmentConfirmedV1Type, integrationEvent.EventId, result);
    }

    /// <summary>A course the student is enrolled in starts soon: remind the student.</summary>
    /// <param name="integrationEvent">The CloudEvent data published by the Enrollment service.</param>
    /// <param name="useCase">The use case creating the notification.</param>
    /// <param name="cancellationToken">Signalled when the sidecar gives up on the call.</param>
    [HttpPost("Enrollment/ClassReminder")]
    public async Task<IActionResult> ClassReminderDue([FromBody] ClassReminderDueV1 integrationEvent,
                                                      [FromServices] NotifyClassStartsSoon useCase,
                                                      CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(useCase);

        var result = await useCase.ExecuteAsync(
            new NotifyClassStartsSoonCommand(integrationEvent.EventId, integrationEvent.StudentId, integrationEvent.CourseTitle,
                                             integrationEvent.CourseStartsAt, integrationEvent.OccurredAt),
            cancellationToken).ConfigureAwait(false);
        return Acknowledge(EnrollmentEvents.ClassReminderDueV1Type, integrationEvent.EventId, result);
    }

    /// <summary>A student received a certificate: tell the student.</summary>
    /// <param name="integrationEvent">The CloudEvent data published by the Grading service.</param>
    /// <param name="useCase">The use case creating the notification.</param>
    /// <param name="cancellationToken">Signalled when the sidecar gives up on the call.</param>
    [HttpPost("Grading/CertificateIssued")]
    public async Task<IActionResult> CertificateIssued([FromBody] CertificateIssuedV1 integrationEvent,
                                                       [FromServices] NotifyCertificateIssued useCase,
                                                       CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(useCase);

        var result = await useCase.ExecuteAsync(
            new NotifyCertificateIssuedCommand(integrationEvent.EventId, integrationEvent.StudentId, integrationEvent.CourseTitle,
                                               integrationEvent.CertificateNumber, integrationEvent.OccurredAt),
            cancellationToken).ConfigureAwait(false);
        return Acknowledge(GradingEvents.CertificateIssuedV1Type, integrationEvent.EventId, result);
    }

    // 200 when processed (or a duplicate), RETRY for a transient failure, DROP for an event that can never succeed.
    private OkObjectResult Acknowledge(string eventType, Guid eventId, FluentResults.Result<bool> result)
    {
        if (result.IsSuccess)
        {
            logger.LogEventProcessed(eventType, eventId, duplicate: !result.Value);
            return Ok(new { status = "SUCCESS" });
        }

        var code = result.Errors.FirstCode();
        if (result.Errors.AreTransient())
        {
            logger.LogEventRetried(eventType, eventId, code);
            return Ok(new { status = "RETRY" });
        }

        logger.LogEventDropped(eventType, eventId, code);
        return Ok(new { status = "DROP" });
    }
}