using Arc4u.Dependency.Attribute;
using Dapr;
using Dapr.Workflow;
using Dev4u.CampusHub.Enrollment.Domain.Enrollments;
using FluentResults;
using Grpc.Core;

namespace Dev4u.CampusHub.Enrollment.Host.Workflows;

/// <summary>Starts the enrollment saga on Dapr Workflow (adapter for the <see cref="IEnrollmentSaga"/> port).</summary>
[Export(typeof(IEnrollmentSaga)), Scoped]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by the Arc4u dependency injection ([Export]).")]
internal sealed class DaprEnrollmentSaga(DaprWorkflowClient workflows, ILogger<DaprEnrollmentSaga> logger) : IEnrollmentSaga
{
    public async Task<Result> StartAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var instanceId = EnrollmentSaga.InstanceIdFor(enrollmentId);
        try
        {
            var state = await workflows.GetWorkflowStateAsync(instanceId, getInputsAndOutputs: false, cancellationToken).ConfigureAwait(false);
            if (!state.Exists)
            {
                await workflows.ScheduleNewWorkflowAsync(nameof(EnrollmentSaga), instanceId, enrollmentId, startTime: null, cancellationToken)
                               .ConfigureAwait(false);
            }

            return Result.Ok();
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
        {
            return Result.Ok(); // a concurrent identical request started it first
        }
        catch (Exception ex) when (ex is RpcException or DaprException or HttpRequestException)
        {
            logger.LogSagaStartFailed(ex, enrollmentId);
            return Result.Fail(EnrollmentErrors.SagaUnavailable(enrollmentId));
        }
    }

    public async Task<Result<EnrollmentSagaState>> GetStateAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        try
        {
            var state = await workflows.GetWorkflowStateAsync(EnrollmentSaga.InstanceIdFor(enrollmentId), getInputsAndOutputs: false,
                                                              cancellationToken).ConfigureAwait(false);
            return Result.Ok(state switch
            {
                { Exists: false } => EnrollmentSagaState.NotFound,
                { IsWorkflowCompleted: true } => EnrollmentSagaState.Ended, // completed, failed or terminated
                _ => EnrollmentSagaState.Running,
            });
        }
        catch (Exception ex) when (ex is RpcException or DaprException or HttpRequestException)
        {
            logger.LogSagaStateUnavailable(ex, enrollmentId);
            return Result.Fail<EnrollmentSagaState>(EnrollmentErrors.SagaUnavailable(enrollmentId));
        }
    }
}
