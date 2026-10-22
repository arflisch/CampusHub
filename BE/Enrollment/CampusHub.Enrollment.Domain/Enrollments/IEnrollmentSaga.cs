using FluentResults;

namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>Port that starts the enrollment saga (implemented with Dapr Workflow by the host).</summary>
public interface IEnrollmentSaga
{
    /// <summary>
    /// Starts the saga for an enrollment. Idempotent: starting an already started saga succeeds without doing
    /// anything. Fails with a transient error when the workflow engine is unreachable.
    /// </summary>
    Task<Result> StartAsync(Guid enrollmentId, CancellationToken cancellationToken);
}
