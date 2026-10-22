using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Grading.ServiceApi;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "{eventName} event received.")]
    public static partial void LogEventReceived(this ILogger logger, string eventName);

}
