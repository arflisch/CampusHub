using Dev4u.CampusHub.Catalog.IntegrationEvents;
using Dev4u.CampusHub.Enrollment.UseCases.Enrollments;
using Dev4u.CampusHub.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dev4u.CampusHub.Enrollment.Host.Subscribers;

/// <summary>
/// Subscriber endpoints called by the Dapr sidecar (routes declared in Dapr/*/CampusHub-Enrollment-*-Subscription.yaml).
/// <para>
/// Only the sidecar may call them: the "Dapr" policy checks the dapr-api-token header against APP_API_TOKEN. The response drives the redelivery: 200 = processed, {"status":"RETRY"} = transient
/// failure (redelivered by the resiliency policy, then dead-lettered), {"status":"DROP"} = permanent failure.
/// </para>
/// </summary>
[ApiController]
[Authorize(Policy = "Dapr")]
[ApiExplorerSettings(GroupName = "dapr")]
[Route("Dapr")]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal",
    Justification = "MVC only discovers public controllers.")]
public sealed partial class DaprController(ILogger<DaprController> logger) : ControllerBase
{
    /// <summary>A teacher cancelled a course: refund and cancel every confirmed enrollment (fan-out).</summary>
    /// <param name="integrationEvent">The CloudEvent data published by the Catalog service.</param>
    /// <param name="useCase">The use case cancelling the enrollments.</param>
    /// <param name="cancellationToken">Signalled when the sidecar gives up on the call.</param>
    [HttpPost("Catalog/CourseCancelled")]
    public async Task<IActionResult> CourseCancelled([FromBody] CourseCancelledV1 integrationEvent,
                                                     [FromServices] CancelEnrollmentsOfCancelledCourse useCase,
                                                     CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(useCase);

        var result = await useCase.ExecuteAsync(integrationEvent.CourseId, cancellationToken).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            LogCourseCancellationProcessed(logger, integrationEvent.CourseId, result.Value);
            return Ok(new { status = "SUCCESS" });
        }

        var code = result.Errors.FirstCode();
        if (result.Errors.AreTransient())
        {
            LogEventRetried(logger, CatalogEvents.CourseCancelledV1Type, integrationEvent.EventId, code);
            return Ok(new { status = "RETRY" });
        }

        LogEventDropped(logger, CatalogEvents.CourseCancelledV1Type, integrationEvent.EventId, code);
        return Ok(new { status = "DROP" });
    }

    [LoggerMessage(EventId = 4400, Level = LogLevel.Information, Message = "Course {CourseId} cancelled: {Count} enrollment(s) refunded and cancelled.")]
    private static partial void LogCourseCancellationProcessed(ILogger logger, Guid courseId, int count);

    [LoggerMessage(EventId = 4401, Level = LogLevel.Warning, Message = "Event {EventType} {EventId} failed transiently ({ErrorCode}): asking Dapr to redeliver.")]
    private static partial void LogEventRetried(ILogger logger, string eventType, Guid eventId, string errorCode);

    [LoggerMessage(EventId = 4402, Level = LogLevel.Warning, Message = "Event {EventType} {EventId} dropped ({ErrorCode}).")]
    private static partial void LogEventDropped(ILogger logger, string eventType, Guid eventId, string errorCode);
}
