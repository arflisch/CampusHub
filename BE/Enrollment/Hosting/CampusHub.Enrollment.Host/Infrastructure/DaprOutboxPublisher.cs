using System.Text.Json;
using Arc4u.Dependency.Attribute;
using Dapr.Client;
using Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

namespace Dev4u.CampusHub.Enrollment.Host.Infrastructure;

/// <summary>Publishes outbox messages on the Enrollment pub/sub component (RabbitMQ) through the Dapr sidecar.</summary>
[Export(typeof(IOutboxPublisher)), Scoped]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the Arc4u dependency injection ([Export]).")]
internal sealed class DaprOutboxPublisher(DaprClient dapr) : IOutboxPublisher
{
    /// <summary>Name of the Dapr pub/sub component scoped to this service (Dapr/*/CampusHub-Enrollment-pubsub-PubSub.yaml).</summary>
    public const string PubSubName = "campushub-enrollment-pubsub-pubsub";

    public async Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        // Publish the JSON object itself (a string payload would be published as a JSON string).
        using var payload = JsonDocument.Parse(message.Payload);
        await dapr.PublishEventAsync(PubSubName, message.Topic, payload.RootElement, new Dictionary<string, string>
        {
            // Stable id: subscribers deduplicate on it. Type: matched by the subscriptions' routing rules.
            ["cloudevent.id"] = message.Id.ToString(),
            ["cloudevent.type"] = message.Type,
        }, cancellationToken).ConfigureAwait(false);
    }
}
