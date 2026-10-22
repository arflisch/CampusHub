using Hangfire;

namespace Dev4u.CampusHub.Enrollment.Jobs;

public static class RegisterJobs
{
    /// <summary>
    /// Registers (or updates) the recurring jobs. Uses the DI-registered manager rather than the static
    /// RecurringJob API, which depends on a global JobStorage that may not be initialized yet.
    /// </summary>
    public static void Register(IRecurringJobManager recurringJobs)
    {
        ArgumentNullException.ThrowIfNull(recurringJobs);
        // CancellationToken.None is a placeholder: Hangfire passes its own token, cancelled at shutdown.
        recurringJobs.AddOrUpdate<ReleaseExpiredReservationsJob>("ReleaseExpiredReservationsJob", job => job.ExecuteAsync(CancellationToken.None), "*/5 * * * *");
        // Hourly with a one-day window: every course gets its reminder about a day before it starts.
        recurringJobs.AddOrUpdate<ClassRemindersJob>("ClassRemindersJob", job => job.ExecuteAsync(CancellationToken.None), Cron.Hourly());
    }
}
