using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.IntegrationEvents;
using Dev4u.CampusHub.Events;
using MongoDB.Bson.Serialization.Attributes;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

/// <summary>
/// An integration event waiting to be published. Written in the same transaction as the aggregate change that
/// produced it, then published (at least once) by the dispatcher.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class OutboxMessage
{
    /// <summary>Integration event id, reused as the CloudEvent id so subscribers can deduplicate.</summary>
    [BsonId]
    public Guid Id { get; set; }

    public string Topic { get; set; } = string.Empty;

    /// <summary>CloudEvent type, used by the subscriptions' routing rules.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The integration event serialized as JSON (web defaults, like DaprClient).</summary>
    public string Payload { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>W3C id of the span that produced the event, restored as parent when publishing.</summary>
    public string? TraceParent { get; set; }

    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>Set when the dispatcher gave up after too many attempts: needs an operator.</summary>
    public DateTime? FailedAtUtc { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }

    /// <summary>Lease taken by a dispatcher instance; also used as the retry delay after a failure.</summary>
    public DateTime? LockedUntilUtc { get; set; }

    public string? LockedBy { get; set; }
}

/// <summary>Maps domain events to the public, versioned integration events of the Enrollment service.</summary>
internal static class OutboxMessages
{
    public static List<OutboxMessage> From(IEnumerable<IDomainEvent> domainEvents) =>
        [.. domainEvents.Select(From).OfType<OutboxMessage>()];

    private static OutboxMessage? From(IDomainEvent domainEvent) => domainEvent switch
    {
        EnrollmentConfirmed confirmed => Create(
            EnrollmentEvents.EnrollmentConfirmedV1Type,
            new EnrollmentConfirmedV1(
                EventId: DeterministicId(confirmed.EnrollmentId, EnrollmentEvents.EnrollmentConfirmedV1Type),
                OccurredAt: confirmed.OccurredAt,
                EnrollmentId: confirmed.EnrollmentId,
                CourseId: confirmed.CourseId,
                CourseTitle: confirmed.CourseTitle,
                StudentId: confirmed.StudentId,
                CourseStartsAt: confirmed.CourseStartsAt,
                CourseEndsAt: confirmed.CourseEndsAt),
            e => e.EventId,
            confirmed.OccurredAt),
        ClassReminderDue reminder => Create(
            EnrollmentEvents.ClassReminderDueV1Type,
            new ClassReminderDueV1(
                EventId: DeterministicId(reminder.EnrollmentId, EnrollmentEvents.ClassReminderDueV1Type),
                OccurredAt: reminder.OccurredAt,
                EnrollmentId: reminder.EnrollmentId,
                CourseId: reminder.CourseId,
                CourseTitle: reminder.CourseTitle,
                CourseStartsAt: reminder.CourseStartsAt,
                StudentId: reminder.StudentId),
            e => e.EventId,
            reminder.OccurredAt),
        _ => null, // domain events that are not published outside the service
    };

    private static OutboxMessage Create<TEvent>(string type, TEvent integrationEvent, Func<TEvent, Guid> idOf, DateTimeOffset occurredAt) => new()
    {
        Id = idOf(integrationEvent),
        Topic = EnrollmentEvents.Topic,
        Type = type,
        Payload = JsonSerializer.Serialize(integrationEvent, JsonSerializerOptions.Web),
        OccurredAtUtc = occurredAt.UtcDateTime,
        TraceParent = Activity.Current?.Id,
    };

    /// <summary>
    /// One enrollment is confirmed once and reminded once: deriving the event id from the aggregate id and the event
    /// type means a replayed step can never produce a second, different event (the outbox insert collides on _id).
    /// </summary>
    private static Guid DeterministicId(Guid aggregateId, string eventType)
    {
        Span<byte> bytes = stackalloc byte[16];
        aggregateId.TryWriteBytes(bytes);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(eventType));
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] ^= digest[i];
        }

        return new Guid(bytes);
    }
}
