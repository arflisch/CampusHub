namespace Dev4u.CampusHub.Events;

/// <summary>
/// Something that happened inside an aggregate. Domain events stay in-process; the services that need to tell
/// other services map them to versioned integration events written to their outbox.
/// </summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAt { get; }
}
