using System.Security.Authentication;
using Arc4u.OAuth2;
using Arc4u.Results;
using Arc4u.Security.Principal;
using Dapr.Workflow;
using Dev4u.CampusHub.Enrollment.UseCases.Enrollments;
using Dev4u.CampusHub.Errors;
using FluentResults;

namespace Dev4u.CampusHub.Enrollment.Host.Workflows;

/// <summary>Thrown by an activity for a transient failure so that the workflow retry policy applies.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S3871:Exception types should be public",
    Justification = "Never caught by type outside this assembly: the workflow runtime only records it as a failure detail.")]
internal sealed class TransientSagaStepException : Exception
{
    public TransientSagaStepException()
    {
    }

    public TransientSagaStepException(string message) : base(message)
    {
    }

    public TransientSagaStepException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Base of the saga activities. Each execution gets its own DI scope running as the service account (outgoing
/// calls carry a client-credentials token), a timeout (activities receive no cancellation token), and the
/// translation from a use-case <see cref="Result"/> to the workflow's contract: success → Succeeded, permanent
/// failure → Failed(code), transient failure → exception (retried by the policy).
/// </summary>
internal abstract class SagaActivity<TInput>(IServiceScopeFactory scopeFactory) : WorkflowActivity<TInput, SagaStepResult>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public override async Task<SagaStepResult> RunAsync(WorkflowActivityContext context, TInput input)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using var _ = scope.ConfigureAwait(false);
        var services = scope.ServiceProvider;

        services.GetRequiredService<IScopedServiceProviderAccessor>().ServiceProvider = services;
        var applicationContext = services.GetRequiredService<IApplicationContext>();
        await services.GetRequiredService<IAppPrincipalFactory>().CreatePrincipalAsync("Service")
            .LogIfFailed()
            .OnFailedAsync(_ => throw new AuthenticationException("Failed to create principal for service account."))
            .OnSuccessNotNull(principal => { applicationContext.SetPrincipal(principal); })
            .ConfigureAwait(false);

        using var timeout = new CancellationTokenSource(Timeout);
        var result = await ExecuteAsync(services, input, timeout.Token).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return SagaStepResult.Ok();
        }

        if (result.Errors.AreTransient())
        {
            throw new TransientSagaStepException($"{GetType().Name} failed transiently: {result.Errors.FirstCode()}");
        }

        return SagaStepResult.Failed(result.Errors.FirstCode());
    }

    protected abstract Task<Result> ExecuteAsync(IServiceProvider services, TInput input, CancellationToken cancellationToken);
}

internal sealed class ReserveSeatActivity(IServiceScopeFactory scopeFactory) : SagaActivity<Guid>(scopeFactory)
{
    protected override Task<Result> ExecuteAsync(IServiceProvider services, Guid input, CancellationToken cancellationToken) =>
        services.GetRequiredService<ReserveSeatForEnrollment>().ExecuteAsync(input, cancellationToken);
}

internal sealed class PayForEnrollmentActivity(IServiceScopeFactory scopeFactory) : SagaActivity<Guid>(scopeFactory)
{
    protected override Task<Result> ExecuteAsync(IServiceProvider services, Guid input, CancellationToken cancellationToken) =>
        services.GetRequiredService<PayForEnrollment>().ExecuteAsync(input, cancellationToken);
}

internal sealed class RefundPaymentActivity(IServiceScopeFactory scopeFactory) : SagaActivity<Guid>(scopeFactory)
{
    protected override Task<Result> ExecuteAsync(IServiceProvider services, Guid input, CancellationToken cancellationToken) =>
        services.GetRequiredService<RefundEnrollmentPayment>().ExecuteAsync(input, cancellationToken);
}

internal sealed class ConfirmEnrollmentActivity(IServiceScopeFactory scopeFactory) : SagaActivity<Guid>(scopeFactory)
{
    protected override Task<Result> ExecuteAsync(IServiceProvider services, Guid input, CancellationToken cancellationToken) =>
        services.GetRequiredService<ConfirmEnrollment>().ExecuteAsync(input, cancellationToken);
}

internal sealed class ReleaseSeatActivity(IServiceScopeFactory scopeFactory) : SagaActivity<Guid>(scopeFactory)
{
    protected override Task<Result> ExecuteAsync(IServiceProvider services, Guid input, CancellationToken cancellationToken) =>
        services.GetRequiredService<ReleaseEnrollmentSeat>().ExecuteAsync(input, cancellationToken);
}

internal sealed class RejectEnrollmentActivity(IServiceScopeFactory scopeFactory) : SagaActivity<RejectEnrollmentInput>(scopeFactory)
{
    protected override Task<Result> ExecuteAsync(IServiceProvider services, RejectEnrollmentInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return services.GetRequiredService<RejectEnrollment>().ExecuteAsync(input.EnrollmentId, input.ReasonCode, cancellationToken);
    }
}
