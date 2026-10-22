using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using FluentResults;

namespace Dev4u.CampusHub.Enrollment.UseCases.Enrollments;

/// <summary>A student reads one of their enrollments (used to follow the saga).</summary>
[Export, Scoped]
public sealed class GetEnrollment(IEnrollmentRepository enrollments)
{
    public async Task<Result<EnrollmentDto>> ExecuteAsync(Guid enrollmentId, string studentId, CancellationToken cancellationToken)
    {
        var enrollment = await enrollments.GetAsync(enrollmentId, cancellationToken).ConfigureAwait(false);

        // Someone else's enrollment answers "not found": no information leaks about other students (BOLA).
        return enrollment is null || !enrollment.BelongsTo(studentId)
            ? Result.Fail<EnrollmentDto>(EnrollmentErrors.NotFound(enrollmentId))
            : Result.Ok(enrollment.ToDto());
    }
}
