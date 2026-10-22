namespace Dev4u.CampusHub.Grading.Host;

internal static partial class LoggerMessages
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Start the Api process.")]
    public static partial void LogStartApiProcess(this ILogger logger);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "The certificate of enrollment {EnrollmentId} could not be announced.")]
    public static partial void LogCertificateAnnouncementFailed(this ILogger logger, Exception exception, Guid enrollmentId);
}
