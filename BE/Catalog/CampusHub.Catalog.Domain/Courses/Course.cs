using FluentResults;

namespace Dev4u.CampusHub.Catalog.Domain.Courses;

/// <summary>
/// A course published by a teacher, with a limited number of seats.
/// <para>
/// The aggregate owns its seat reservations so that "is there still a seat?" is answered inside one document and
/// protected by optimistic concurrency (<see cref="Version"/>): two students racing for the last seat cannot both
/// win. Every state change goes through a method returning a <see cref="Result"/>; expected refusals (course
/// full, not published, ...) are failures with a stable code, not exceptions.
/// </para>
/// </summary>
public sealed class Course
{
    public const int MaxCapacity = 500;

    private readonly List<SeatReservation> _reservations;

    private Course(Guid id, string code, string title, string teacherId, int capacity, CourseSchedule schedule, CourseStatus status,
                   DateTimeOffset createdAt, DateTimeOffset? publishedAt, IEnumerable<SeatReservation> reservations, long version)
    {
        Id = id;
        Code = code;
        Title = title;
        TeacherId = teacherId;
        Capacity = capacity;
        Schedule = schedule;
        Status = status;
        CreatedAt = createdAt;
        PublishedAt = publishedAt;
        _reservations = [.. reservations];
        Version = version;
    }

    public Guid Id { get; }

    /// <summary>Short unique code, upper case (e.g. DOTNET-101).</summary>
    public string Code { get; }

    public string Title { get; }

    /// <summary>Identity provider subject of the teacher who owns the course.</summary>
    public string TeacherId { get; }

    public int Capacity { get; }

    public CourseSchedule Schedule { get; }

    public CourseStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>Optimistic concurrency token, incremented by the repository on every successful save.</summary>
    public long Version { get; private set; }

    public IReadOnlyList<SeatReservation> Reservations => _reservations;

    public static Result<Course> Create(Guid id, string? code, string? title, int capacity, DateTimeOffset startsAt, DateTimeOffset endsAt,
                                        string teacherId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teacherId);

        var errors = new List<IError>();
        var trimmedCode = code?.Trim() ?? string.Empty;
        var trimmedTitle = title?.Trim() ?? string.Empty;

        if (trimmedCode.Length is < 3 or > 20)
        {
            errors.Add(CourseErrors.InvalidCode());
        }

        if (trimmedTitle.Length is 0 or > 200)
        {
            errors.Add(CourseErrors.InvalidTitle());
        }

        if (capacity is < 1 or > MaxCapacity)
        {
            errors.Add(CourseErrors.InvalidCapacity());
        }

        var schedule = CourseSchedule.Create(startsAt, endsAt, now);
        errors.AddRange(schedule.Errors);

        if (errors.Count > 0)
        {
            return Result.Fail<Course>(errors);
        }

        return Result.Ok(new Course(id, trimmedCode.ToUpperInvariant(), trimmedTitle, teacherId, capacity, schedule.Value,
                                    CourseStatus.Draft, now, publishedAt: null, reservations: [], version: 0));
    }

    /// <summary>Rebuilds a course from storage. Persistence only: no rule is checked.</summary>
    public static Course Rehydrate(Guid id, string code, string title, string teacherId, int capacity, CourseSchedule schedule,
                                   CourseStatus status, DateTimeOffset createdAt, DateTimeOffset? publishedAt,
                                   IEnumerable<SeatReservation> reservations, long version) =>
        new(id, code, title, teacherId, capacity, schedule, status, createdAt, publishedAt, reservations, version);

    public bool IsTaughtBy(string userId) => string.Equals(TeacherId, userId, StringComparison.Ordinal);

    public int SeatsTaken(DateTimeOffset now) => _reservations.Count(r => r.Occupies(now));

    public int SeatsLeft(DateTimeOffset now) => Math.Max(0, Capacity - SeatsTaken(now));

    public Result Publish(string userId, DateTimeOffset now)
    {
        if (!IsTaughtBy(userId))
        {
            return Result.Fail(CourseErrors.NotOwner(Id));
        }

        if (Status == CourseStatus.Published)
        {
            return Result.Ok(); // publishing twice is harmless
        }

        if (Status != CourseStatus.Draft)
        {
            return Result.Fail(CourseErrors.InvalidTransition(Status, CourseStatus.Published));
        }

        Status = CourseStatus.Published;
        PublishedAt = now;
        return Result.Ok();
    }

    /// <summary>
    /// Holds a seat for a student. Idempotent on <paramref name="idempotencyKey"/>: a retried call (the saga's
    /// activities run at least once) gets the reservation it already obtained instead of a second seat.
    /// </summary>
    public Result<SeatReservation> ReserveSeat(string studentId, string idempotencyKey, DateTimeOffset now, TimeSpan holdDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var existing = _reservations.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
        {
            return existing.Status == ReservationStatus.Released
                ? Result.Fail<SeatReservation>(CourseErrors.ReservationReleased(existing.Id))
                : Result.Ok(existing);
        }

        if (Status != CourseStatus.Published)
        {
            return Result.Fail<SeatReservation>(CourseErrors.NotOpen(Id));
        }

        if (Schedule.HasStarted(now))
        {
            return Result.Fail<SeatReservation>(CourseErrors.Started(Id));
        }

        if (_reservations.Any(r => string.Equals(r.StudentId, studentId, StringComparison.Ordinal) && r.Occupies(now)))
        {
            return Result.Fail<SeatReservation>(CourseErrors.AlreadyEnrolled(Id));
        }

        if (SeatsLeft(now) == 0)
        {
            return Result.Fail<SeatReservation>(CourseErrors.Full(Id));
        }

        var reservation = SeatReservation.Hold(Guid.CreateVersion7(now), studentId, idempotencyKey, now, holdDuration);
        _reservations.Add(reservation);
        return Result.Ok(reservation);
    }

    /// <summary>Turns a held seat into a confirmed one. Confirming twice succeeds; an expired hold cannot be confirmed.</summary>
    public Result ConfirmSeat(Guid reservationId, DateTimeOffset now)
    {
        var reservation = _reservations.FirstOrDefault(r => r.Id == reservationId);
        return reservation is null
            ? Result.Fail(CourseErrors.ReservationNotFound(reservationId))
            : reservation.Confirm(now);
    }

    /// <summary>
    /// Gives a seat back (saga compensation). Idempotent: releasing an unknown or already released reservation
    /// succeeds, because a compensation must be safe to replay.
    /// </summary>
    public Result ReleaseSeat(Guid reservationId)
    {
        _reservations.FirstOrDefault(r => r.Id == reservationId)?.Release();
        return Result.Ok();
    }

    /// <summary>Called by the repository once the new version is stored. Persistence only.</summary>
    public void MarkPersisted() => Version++;
}
