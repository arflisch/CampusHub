using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 3100, Level = LogLevel.Debug, Message = "Persisted {Count} document(s) to Enrollments.")]
    public static partial void LogDocumentsPersisted(this ILogger logger, int count);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Warning, Message = "No document matched the filter on {Collection} in Enrollments.")]
    public static partial void LogNoDocumentMatched(this ILogger logger, string collection);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Information, Message = "Outbox message {MessageId} ({EventType}) published.")]
    public static partial void LogOutboxPublished(this ILogger logger, Guid messageId, string eventType);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Warning, Message = "Publishing outbox message {MessageId} failed (attempt {Attempt}).")]
    public static partial void LogOutboxPublishFailed(this ILogger logger, Exception exception, Guid messageId, int attempt);
}
