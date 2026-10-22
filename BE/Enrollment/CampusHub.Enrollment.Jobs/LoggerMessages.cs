using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Enrollment.Jobs;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "Job {jobName} is executed successfully.")]
    public static partial void LogJobExecuted(this ILogger logger, string jobName);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Job {jobName}: {examined} examined, {handled} handled, {skipped} skipped, {failed} failed (retried next run).")]
    public static partial void LogMaintenanceReport(this ILogger logger, string jobName, int examined, int handled, int skipped, int failed);
}
