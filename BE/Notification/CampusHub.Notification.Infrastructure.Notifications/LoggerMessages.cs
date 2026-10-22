using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Notification.Infrastructure.Notifications;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Debug, Message = "Persisted {Count} document(s) to Notifications.")]
    public static partial void LogDocumentsPersisted(this ILogger logger, int count);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning, Message = "No document matched the filter on {Collection} in Notifications.")]
    public static partial void LogNoDocumentMatched(this ILogger logger, string collection);
}
