using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Catalog.Domain.Courses;
using Dev4u.CampusHub.Catalog.UseCases.Common;
using FluentResults;

namespace Dev4u.CampusHub.Catalog.UseCases.Courses;

public sealed record ReserveSeatCommand(string StudentId, string IdempotencyKey);

/// <summary>
/// Seat operations used by the Enrollment saga through the service API. All three are idempotent, because the
/// saga's activities are retried, and all three retry on optimistic-concurrency conflicts, because during an
/// enrollment rush many students hit the same course document at the same time.
/// </summary>
[Export, Scoped]
public sealed class ReserveSeat(ICourseRepository courses, TimeProvider timeProvider)
{
    public async Task<Result<SeatReservationDto>> ExecuteAsync(Guid courseId, ReserveSeatCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await ConcurrencyRetry.ExecuteAsync(async ct =>
        {
            var course = await courses.GetAsync(courseId, ct).ConfigureAwait(false);
            if (course is null)
            {
                return Result.Fail<SeatReservationDto>(CourseErrors.NotFound(courseId));
            }

            var reservation = course.ReserveSeat(command.StudentId, command.IdempotencyKey, timeProvider.GetUtcNow(), Constants.SeatHoldDuration);
            if (reservation.IsFailed)
            {
                return reservation.ToResult<SeatReservationDto>();
            }

            var saved = await courses.SaveAsync(course, ct).ConfigureAwait(false);
            return saved.IsFailed ? saved.ToResult<SeatReservationDto>() : Result.Ok(reservation.Value.ToDto(course));
        }, cancellationToken).ConfigureAwait(false);
    }
}

[Export, Scoped]
public sealed class ConfirmSeat(ICourseRepository courses, TimeProvider timeProvider)
{
    public async Task<Result> ExecuteAsync(Guid courseId, Guid reservationId, CancellationToken cancellationToken)
    {
        var result = await ConcurrencyRetry.ExecuteAsync(async ct =>
        {
            var course = await courses.GetAsync(courseId, ct).ConfigureAwait(false);
            if (course is null)
            {
                return Result.Fail<bool>(CourseErrors.NotFound(courseId));
            }

            var confirmed = course.ConfirmSeat(reservationId, timeProvider.GetUtcNow());
            if (confirmed.IsFailed)
            {
                return confirmed.ToResult<bool>();
            }

            var saved = await courses.SaveAsync(course, ct).ConfigureAwait(false);
            return saved.ToResult(true);
        }, cancellationToken).ConfigureAwait(false);

        return result.ToResult();
    }
}

[Export, Scoped]
public sealed class ReleaseSeat(ICourseRepository courses)
{
    public async Task<Result> ExecuteAsync(Guid courseId, Guid reservationId, CancellationToken cancellationToken)
    {
        var result = await ConcurrencyRetry.ExecuteAsync(async ct =>
        {
            var course = await courses.GetAsync(courseId, ct).ConfigureAwait(false);
            if (course is null)
            {
                return Result.Ok(true); // nothing to give back: a compensation must be safe to replay
            }

            course.ReleaseSeat(reservationId);
            var saved = await courses.SaveAsync(course, ct).ConfigureAwait(false);
            return saved.ToResult(true);
        }, cancellationToken).ConfigureAwait(false);

        return result.ToResult();
    }
}
