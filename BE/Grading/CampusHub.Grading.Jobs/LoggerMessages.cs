using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Grading.Jobs;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Job {jobName} is executed successfully.")]
    public static partial void LogJobExecuted(this ILogger logger, string jobName);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Certificates: {issued} issued, {announced} announced, {failed} failed (retried next run).")]
    public static partial void LogCertificatesReport(this ILogger logger, int issued, int announced, int failed);
}
