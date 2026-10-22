namespace Dev4u.CampusHub.Enrollment.UseCases.Common;

internal static class Constants
{
    /// <summary>
    /// Name of this service as reported by the environment use case.
    /// </summary>
    public const string ServiceName = "Enrollment";

    /// <summary>Attempts made when a save loses an optimistic-concurrency race.</summary>
    public const int ConcurrencyAttempts = 3;
}
