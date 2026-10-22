using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Grading.ServiceApi;

/// <summary>
/// Subscriber endpoints called by the Dapr sidecar (routes declared in Dapr/*/CampusHub-Grading-*-Subscription.yaml).
/// Only the sidecar may call them (dapr-api-token checked against APP_API_TOKEN). Exceptions propagate (500) so
/// that Dapr redelivers the event instead of losing it.
/// </summary>
[ApiController]
[Authorize(Policy = "Dapr")]
[ApiExplorerSettings(GroupName = "dapr")]
[Route("Dapr")]
public sealed class DaprController(ILogger<DaprController> logger) : ControllerBase
{
    /// <summary>
    /// A student joined a course. The grade book entry will be created here in the next slice; for now the event
    /// is acknowledged and traced.
    /// </summary>
    /// <param name="integrationEvent">The CloudEvent data published by the Enrollment service.</param>
    [HttpPost("Enrollment/Confirmed")]
    public IActionResult EnrollmentConfirmed([FromBody] System.Text.Json.JsonElement integrationEvent)
    {
        logger.LogEventReceived("Enrollment confirmed");
        return Ok();
    }
}
