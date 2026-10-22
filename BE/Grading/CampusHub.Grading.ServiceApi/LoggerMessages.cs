using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Grading.ServiceApi;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "{eventName} event received.")]
    public static partial void LogEventReceived(this ILogger logger, string eventName);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Event {EventType} {EventId} processed (duplicate: {Duplicate}).")]
    public static partial void LogEventProcessed(this ILogger logger, string eventType, Guid eventId, bool duplicate);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Event {EventType} {EventId} failed transiently ({ErrorCode}): asking Dapr to redeliver.")]
    public static partial void LogEventRetried(this ILogger logger, string eventType, Guid eventId, string errorCode);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Event {EventType} {EventId} dropped ({ErrorCode}).")]
    public static partial void LogEventDropped(this ILogger logger, string eventType, Guid eventId, string errorCode);

}
