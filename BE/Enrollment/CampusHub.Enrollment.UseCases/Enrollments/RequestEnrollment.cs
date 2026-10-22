using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using FluentResults;

namespace Dev4u.CampusHub.Enrollment.UseCases.Enrollments;

/// <summary>
/// A student asks to join a course. The request is stored, then the saga is started; the student polls the
/// enrollment until it is Confirmed or Rejected.
/// <para>
/// Idempotent on <see cref="RequestEnrollmentCommand.RequestId"/>: a client that times out and retries gets the
/// same enrollment, and the saga is (re)started if the first attempt could not reach the workflow engine.
/// </para>
/// </summary>
[Export, Scoped]
public sealed class RequestEnrollment(IEnrollmentRepository enrollments, IEnrollmentSaga saga, TimeProvider timeProvider)
{
    public async Task<Result<EnrollmentDto>> ExecuteAsync(RequestEnrollmentCommand command, string studentId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var enrollment = await enrollments.GetAsync(command.RequestId, cancellationToken).ConfigureAwait(false);
        if (enrollment is not null && !enrollment.BelongsTo(studentId))
        {
            return Result.Fail<EnrollmentDto>(EnrollmentErrors.RequestIdTaken(command.RequestId));
        }

        if (enrollment is null)
        {
            var requested = StudentEnrollment.Request(command.RequestId, command.CourseId, studentId, timeProvider.GetUtcNow());
            if (requested.IsFailed)
            {
                return requested.ToResult<EnrollmentDto>();
            }

            var added = await enrollments.AddAsync(requested.Value, cancellationToken).ConfigureAwait(false);
            if (added.IsFailed)
            {
                return added.ToResult<EnrollmentDto>();
            }

            enrollment = requested.Value;
        }

        if (enrollment.Status == EnrollmentStatus.Requested)
        {
            var started = await saga.StartAsync(enrollment.Id, cancellationToken).ConfigureAwait(false);
            if (started.IsFailed)
            {
                return started.ToResult<EnrollmentDto>();
            }
        }

        return Result.Ok(enrollment.ToDto());
    }
}
