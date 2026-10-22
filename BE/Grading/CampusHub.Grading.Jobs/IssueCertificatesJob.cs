using System.Diagnostics;
using System.Security.Authentication;
using Arc4u.Dependency.Attribute;
using Arc4u.Diagnostics;
using Arc4u.OAuth2;
using Arc4u.Results;
using Arc4u.Security.Principal;
using Dev4u.CampusHub.Grading.UseCases.Certificates;
using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Grading.Jobs;

/// <summary>
/// Every hour: issues the certificates of the courses that ended and announces them (see
/// <see cref="IssueCertificates"/>). What fails is retried by the next run.
/// </summary>
[Export]
public class IssueCertificatesJob(
            IServiceProvider scopedServiceProvider,
            IScopedServiceProviderAccessor accessor,
            IAppPrincipalFactory principalFactory,
            ILogger<IssueCertificatesJob> logger,
            IActivitySourceFactory activitySourceFactory,
            IApplicationContext applicationContext,
            IssueCertificates useCase)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activitySource = activitySourceFactory.Get("CampusHub.Jobs");

            accessor.ServiceProvider = scopedServiceProvider;

            using var activity = activitySource?.StartActivity("IssueCertificatesJob", ActivityKind.Producer);

            // Create a principal based on the service account.
            await principalFactory.CreatePrincipalAsync("Service")
                .LogIfFailed()
                .OnFailedAsync(_ => throw new AuthenticationException("Failed to create principal for service account."))
                .OnSuccessNotNull(principal => { applicationContext.SetPrincipal(principal); })
                .ConfigureAwait(false);

            var report = await useCase.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            logger.Technical().LogCertificatesReport(report.Issued, report.Announced, report.Failed);

            logger.Technical().LogJobExecuted("IssueCertificatesJob");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged for the trace, then rethrown so Hangfire's retry policy applies: swallowing the exception
            // would mark the job as succeeded.
            logger.Technical().LogException(ex);
            throw;
        }
    }
}
