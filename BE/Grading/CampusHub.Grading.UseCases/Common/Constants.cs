namespace Dev4u.CampusHub.Grading.UseCases.Common;

internal static class Constants
{
    /// <summary>
    /// Name of this service as reported by the environment use case.
    /// </summary>
    public const string ServiceName = "Grading";
    /// <summary>
    /// Name of this service as reported by the environment use case.
    /// </summary>
    public const string PubsubTopic = "campushub-grading-pubsub-pubsub";
    /// <summary>
    /// Name of this service as reported by the environment use case.
    /// </summary>
    public const string PubsubTopicName = "pubsub";

    /// <summary>Participations handled per run of the certificates job; the next run takes the rest.</summary>
    public const int MaintenanceBatchSize = 100;

    /// <summary>Attempts made when a save loses an optimistic-concurrency race.</summary>
    public const int ConcurrencyAttempts = 3;
}
