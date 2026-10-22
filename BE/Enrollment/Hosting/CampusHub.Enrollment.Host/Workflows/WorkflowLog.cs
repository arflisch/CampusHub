namespace Dev4u.CampusHub.Enrollment.Host.Workflows;

internal static partial class WorkflowLog
{
    [LoggerMessage(EventId = 4100, Level = LogLevel.Warning, Message = "Enrollment saga {EnrollmentId}: step {Step} failed after its retries.")]
    public static partial void LogSagaStepFailed(this ILogger logger, Exception exception, Guid enrollmentId, string step);

    [LoggerMessage(EventId = 4101, Level = LogLevel.Error, Message = "The enrollment saga for {EnrollmentId} could not be started.")]
    public static partial void LogSagaStartFailed(this ILogger logger, Exception exception, Guid enrollmentId);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Warning, Message = "The state of the enrollment saga for {EnrollmentId} could not be read.")]
    public static partial void LogSagaStateUnavailable(this ILogger logger, Exception exception, Guid enrollmentId);
}
