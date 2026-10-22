using FluentResults;

namespace Dev4u.CampusHub.Catalog.Domain.Courses;

/// <summary>
/// When a course runs. Enrollments close when it starts; Enrollment sends reminders before the start and Grading
/// issues the certificates after the end, from the dates carried by the enrollment events.
/// </summary>
public sealed record CourseSchedule
{
    public static readonly TimeSpan MaxDuration = TimeSpan.FromDays(366);

    private CourseSchedule(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    public DateTimeOffset StartsAt { get; }

    public DateTimeOffset EndsAt { get; }

    /// <summary>A new schedule: it starts in the future and lasts at most <see cref="MaxDuration"/>.</summary>
    public static Result<CourseSchedule> Create(DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset now)
    {
        if (startsAt <= now)
        {
            return Result.Fail(CourseErrors.InvalidSchedule("A course must start in the future."));
        }

        if (endsAt <= startsAt)
        {
            return Result.Fail(CourseErrors.InvalidSchedule("A course must end after it starts."));
        }

        return endsAt - startsAt > MaxDuration
            ? Result.Fail(CourseErrors.InvalidSchedule($"A course lasts at most {MaxDuration.TotalDays} days."))
            : Result.Ok(new CourseSchedule(startsAt, endsAt));
    }

    /// <summary>Rebuilds a schedule from storage. Persistence only: no rule is checked.</summary>
    public static CourseSchedule Rehydrate(DateTimeOffset startsAt, DateTimeOffset endsAt) => new(startsAt, endsAt);

    public bool HasStarted(DateTimeOffset now) => now >= StartsAt;
}
