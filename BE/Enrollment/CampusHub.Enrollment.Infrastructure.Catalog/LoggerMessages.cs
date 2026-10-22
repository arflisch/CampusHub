using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Catalog;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 3200, Level = LogLevel.Warning, Message = "Catalog {Operation} call failed: {Reason}")]
    public static partial void LogCatalogCallFailed(this ILogger logger, Exception exception, string operation, string reason);
}
