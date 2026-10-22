using Arc4u.Dependency.Attribute;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using FluentResults;

namespace Dev4u.CampusHub.Enrollment.UseCases.Enrollments;

/// <summary>The caller's latest enrollments, newest first (the student follows the sagas from there).</summary>
[Export, Scoped]
public sealed class ListMyEnrollments(IEnrollmentRepository enrollments)
{
    public const int MaxCount = 50;

    public async Task<Result<IReadOnlyList<EnrollmentDto>>> ExecuteAsync(string studentId, CancellationToken cancellationToken)
    {
        var mine = await enrollments.ListForStudentAsync(studentId, MaxCount, cancellationToken).ConfigureAwait(false);
        return Result.Ok<IReadOnlyList<EnrollmentDto>>([.. mine.Select(e => e.ToDto())]);
    }
}
