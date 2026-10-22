namespace Dev4u.CampusHub.Catalog.UseCases.Common;

internal static class Constants
{
    /// <summary>
    /// Name of this service as reported by the environment use case.
    /// </summary>
    public const string ServiceName = "Catalog";

    /// <summary>
    /// How long a seat stays held while the enrollment saga runs. Long enough for a payment step, short enough
    /// for an abandoned saga to give the seat back quickly.
    /// </summary>
    public static readonly TimeSpan SeatHoldDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Attempts made when a save loses an optimistic-concurrency race (e.g. many students reserving at once).
    /// </summary>
    public const int ConcurrencyAttempts = 5;
}
