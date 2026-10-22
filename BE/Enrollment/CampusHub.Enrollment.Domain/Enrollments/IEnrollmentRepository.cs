using FluentResults;

namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>
/// Port implemented by the MongoDB adapter.
/// <para>
/// <see cref="AddAsync"/> and <see cref="SaveAsync"/> write the aggregate and its pending domain events (as
/// outbox messages) in one MongoDB transaction: either both are stored or neither is, so an integration event
/// can never be lost or published for a change that was rolled back. Saving is conditional on
/// <see cref="StudentEnrollment.Version"/> (optimistic concurrency).
/// </para>
/// </summary>
public interface IEnrollmentRepository
{
    Task<StudentEnrollment?> GetAsync(Guid enrollmentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StudentEnrollment>> ListForStudentAsync(string studentId, int take, CancellationToken cancellationToken);

    Task<Result> AddAsync(StudentEnrollment enrollment, CancellationToken cancellationToken);

    Task<Result> SaveAsync(StudentEnrollment enrollment, CancellationToken cancellationToken);
}
