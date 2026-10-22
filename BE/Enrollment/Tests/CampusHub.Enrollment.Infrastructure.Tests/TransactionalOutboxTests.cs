using System.Text.Json;
using AwesomeAssertions;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;
using Dev4u.CampusHub.Enrollment.IntegrationEvents;
using Dev4u.CampusHub.Errors;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using Xunit;

namespace Dev4u.CampusHub.Enrollment.Infrastructure.Tests;

public class TransactionalOutboxTests(MongoReplicaSetFixture mongo) : IClassFixture<MongoReplicaSetFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    private static StudentEnrollment ReservedEnrollment()
    {
        var enrollment = StudentEnrollment.Request(Guid.NewGuid(), Guid.NewGuid(), "student-1", Now).Value;
        enrollment.MarkSeatReserved(new SeatHold(Guid.NewGuid(), "Intro to .NET", Now.AddDays(7), Now.AddDays(37)), Now);
        return enrollment;
    }

    [Fact]
    public async Task Confirming_stores_the_enrollment_and_its_event_together()
    {
        var factory = mongo.CreateClientFactory();
        var repository = new MongoEnrollmentRepository(factory, NullLogger<MongoEnrollmentRepository>.Instance);
        var enrollment = ReservedEnrollment();
        (await repository.AddAsync(enrollment, CancellationToken.None)).IsSuccess.Should().BeTrue();

        enrollment.Confirm(Now);
        var saved = await repository.SaveAsync(enrollment, CancellationToken.None);

        saved.IsSuccess.Should().BeTrue();
        enrollment.DomainEvents.Should().BeEmpty("they are in the outbox now");
        (await repository.GetAsync(enrollment.Id, CancellationToken.None))!.Status.Should().Be(EnrollmentStatus.Confirmed);

        var message = await factory.GetCollection<OutboxMessage>().Find(_ => true).SingleAsync();
        message.Type.Should().Be(EnrollmentEvents.EnrollmentConfirmedV1Type);
        message.Topic.Should().Be(EnrollmentEvents.Topic);
        message.ProcessedAtUtc.Should().BeNull();
        var payload = JsonSerializer.Deserialize<EnrollmentConfirmedV1>(message.Payload, JsonSerializerOptions.Web)!;
        payload.EventId.Should().Be(message.Id);
        payload.EnrollmentId.Should().Be(enrollment.Id);
        payload.CourseTitle.Should().Be("Intro to .NET");
        payload.CourseStartsAt.Should().Be(Now.AddDays(7));
        payload.CourseEndsAt.Should().Be(Now.AddDays(37));
    }

    [Fact]
    public async Task A_class_reminder_is_found_in_its_window_and_written_once_to_the_outbox()
    {
        var factory = mongo.CreateClientFactory();
        var repository = new MongoEnrollmentRepository(factory, NullLogger<MongoEnrollmentRepository>.Instance);
        var enrollment = ReservedEnrollment(); // the course starts a week after Now
        await repository.AddAsync(enrollment, CancellationToken.None);
        enrollment.Confirm(Now);
        await repository.SaveAsync(enrollment, CancellationToken.None);
        var dayBefore = Now.AddDays(6).AddHours(12);

        (await repository.ListDueForReminderAsync(Now, Now.AddDays(1), 10, CancellationToken.None)).Should().BeEmpty("the course starts in a week");
        var due = await repository.ListDueForReminderAsync(dayBefore, dayBefore.AddDays(1), 10, CancellationToken.None);
        due.Should().ContainSingle();
        due[0].RemindOfClass(dayBefore).IsSuccess.Should().BeTrue();
        (await repository.SaveAsync(due[0], CancellationToken.None)).IsSuccess.Should().BeTrue();

        (await repository.ListDueForReminderAsync(dayBefore, dayBefore.AddDays(1), 10, CancellationToken.None)).Should().BeEmpty("reminded once");
        var message = await factory.GetCollection<OutboxMessage>().Find(m => m.Type == EnrollmentEvents.ClassReminderDueV1Type).SingleAsync();
        var payload = JsonSerializer.Deserialize<ClassReminderDueV1>(message.Payload, JsonSerializerOptions.Web)!;
        payload.EventId.Should().Be(message.Id);
        payload.CourseStartsAt.Should().Be(Now.AddDays(7));
        payload.StudentId.Should().Be("student-1");
    }

    [Fact]
    public async Task Stalled_enrollments_are_the_unsettled_ones_untouched_since_the_limit()
    {
        var factory = mongo.CreateClientFactory();
        var repository = new MongoEnrollmentRepository(factory, NullLogger<MongoEnrollmentRepository>.Instance);
        var waiting = ReservedEnrollment();
        var confirmed = ReservedEnrollment();
        await repository.AddAsync(waiting, CancellationToken.None);
        await repository.AddAsync(confirmed, CancellationToken.None);
        confirmed.Confirm(Now);
        await repository.SaveAsync(confirmed, CancellationToken.None);

        var stalled = await repository.ListStalledAsync(Now.AddMinutes(1), 10, CancellationToken.None);

        stalled.Select(e => e.Id).Should().Equal(waiting.Id);
        (await repository.ListStalledAsync(Now, 10, CancellationToken.None)).Should().BeEmpty("it changed at the limit, not before");
    }

    [Fact]
    public async Task A_stale_copy_is_rejected_and_writes_no_event()
    {
        var factory = mongo.CreateClientFactory();
        var repository = new MongoEnrollmentRepository(factory, NullLogger<MongoEnrollmentRepository>.Instance);
        var enrollment = ReservedEnrollment();
        await repository.AddAsync(enrollment, CancellationToken.None);

        var first = (await repository.GetAsync(enrollment.Id, CancellationToken.None))!;
        var stale = (await repository.GetAsync(enrollment.Id, CancellationToken.None))!;
        first.Confirm(Now);
        (await repository.SaveAsync(first, CancellationToken.None)).IsSuccess.Should().BeTrue();

        stale.Confirm(Now);
        var result = await repository.SaveAsync(stale, CancellationToken.None);

        result.Errors.HasCode(EnrollmentErrors.ConcurrencyConflictCode).Should().BeTrue();
        (await factory.GetCollection<OutboxMessage>().CountDocumentsAsync(_ => true)).Should().Be(1, "the losing transaction was rolled back");
    }

    [Fact]
    public async Task The_dispatcher_publishes_marks_processed_and_backs_off_on_failure()
    {
        var factory = mongo.CreateClientFactory();
        var outbox = factory.GetCollection<OutboxMessage>();
        var ok = NewMessage();
        var broken = NewMessage();
        await outbox.InsertManyAsync([ok, broken]);
        var publisher = new FakePublisher(failingId: broken.Id);
        var processor = new OutboxProcessor(factory, publisher, TimeProvider.System, NullLogger<OutboxProcessor>.Instance);

        var published = await processor.ProcessBatchAsync(CancellationToken.None);

        published.Should().Be(1);
        publisher.Published.Should().Equal(ok.Id);
        (await outbox.Find(m => m.Id == ok.Id).SingleAsync()).ProcessedAtUtc.Should().NotBeNull();
        var failed = await outbox.Find(m => m.Id == broken.Id).SingleAsync();
        failed.ProcessedAtUtc.Should().BeNull();
        failed.Attempts.Should().Be(1);
        failed.LastError.Should().Be("broker down");
        failed.LockedUntilUtc.Should().BeAfter(DateTime.UtcNow, "the failed message waits for its backoff");

        // Nothing is claimable right now: the processed one is done, the failed one is backing off.
        (await processor.ProcessBatchAsync(CancellationToken.None)).Should().Be(0);
        publisher.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Creating_the_indexes_twice_is_harmless()
    {
        var factory = mongo.CreateClientFactory();

        await EnrollmentIndexes.EnsureAsync(factory, CancellationToken.None);
        await EnrollmentIndexes.EnsureAsync(factory, CancellationToken.None);

        var names = await (await factory.GetCollection<OutboxMessage>().Indexes.ListAsync()).ToListAsync();
        names.Select(i => i["name"].AsString).Should().Contain(["pending", "processed_ttl"]);
    }

    private static OutboxMessage NewMessage() => new()
    {
        Id = Guid.NewGuid(),
        Topic = EnrollmentEvents.Topic,
        Type = EnrollmentEvents.EnrollmentConfirmedV1Type,
        Payload = "{}",
        OccurredAtUtc = DateTime.UtcNow,
    };

    private sealed class FakePublisher(Guid failingId) : IOutboxPublisher
    {
        public List<Guid> Published { get; } = [];

        public int Attempts { get; private set; }

        public Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken)
        {
            Attempts++;
            if (message.Id == failingId)
            {
                throw new InvalidOperationException("broker down");
            }

            Published.Add(message.Id);
            return Task.CompletedTask;
        }
    }
}
