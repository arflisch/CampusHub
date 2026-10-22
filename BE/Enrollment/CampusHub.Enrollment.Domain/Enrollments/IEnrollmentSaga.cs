using FluentResults;

namespace Dev4u.CampusHub.Enrollment.Domain.Enrollments;

/// <summary>What the workflow engine knows about the saga of an enrollment.</summary>
public enum EnrollmentSagaState
{
    /// <summary>Never started, or purged.</summary>
    NotFound,

    /// <summary>Pending, running or suspended: the saga still owns the enrollment.</summary>
    Running,

    /// <summary>Completed, failed or terminated: nothing will move the enrollment any more.</summary>
    Ended,
}

/// <summary>Port to the enrollment saga (implemented with Dapr Workflow by the host).</summary>
public interface IEnrollmentSaga
{
    /// <summary>
    /// Starts the saga for an enrollment. Idempotent: starting an already started saga succeeds without doing
    /// anything. Fails with a transient error when the workflow engine is unreachable.
    /// </summary>
    Task<Result> StartAsync(Guid enrollmentId, CancellationToken cancellationToken);

    /// <summary>State of the saga of an enrollment. Fails with a transient error when the workflow engine is unreachable.</summary>
    Task<Result<EnrollmentSagaState>> GetStateAsync(Guid enrollmentId, CancellationToken cancellationToken);
}
