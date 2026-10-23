using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Billing;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 3300, Level = LogLevel.Warning, Message = "Billing {Operation} call failed: {Reason}")]
    public static partial void LogBillingCallFailed(this ILogger logger, Exception exception, string operation, string reason);
}
