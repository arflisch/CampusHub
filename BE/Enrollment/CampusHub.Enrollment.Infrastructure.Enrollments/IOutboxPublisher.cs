namespace Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

/// <summary>
/// Sends one outbox message to the broker. Implemented by the host with Dapr pub/sub; throws when the message
/// could not be published so the dispatcher records the failure and retries later.
/// </summary>
public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken);
}
