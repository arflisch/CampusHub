namespace Dev4u.CampusHub.Enrollment.UseCases.Common;

internal static class Constants
{
    /// <summary>
    /// Name of this service as reported by the environment use case.
    /// </summary>
    public const string ServiceName = "Enrollment";

    /// <summary>Attempts made when a save loses an optimistic-concurrency race.</summary>
    public const int ConcurrencyAttempts = 3;

    /// <summary>
    /// An unsettled enrollment untouched for this long has lost its saga: longer than the seat hold in Catalog
    /// (15 minutes) plus the saga's retries.
    /// </summary>
    public static readonly TimeSpan StalledAfter = TimeSpan.FromMinutes(20);

    /// <summary>Students are reminded of a course this long before it starts.</summary>
    public static readonly TimeSpan ReminderLeadTime = TimeSpan.FromHours(24);

    /// <summary>Enrollments handled per maintenance run; the next run takes the rest.</summary>
    public const int MaintenanceBatchSize = 100;
}
