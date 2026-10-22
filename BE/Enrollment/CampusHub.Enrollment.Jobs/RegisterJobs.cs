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
        recurringJobs.AddOrUpdate<ReleaseExpiredReservationsJob>("ReleaseExpiredReservationsJob", job => job.ExecuteAsync(), "*/5 * * * *");
        recurringJobs.AddOrUpdate<ClassRemindersJob>("ClassRemindersJob", job => job.ExecuteAsync(), Cron.Daily());
    }
}
