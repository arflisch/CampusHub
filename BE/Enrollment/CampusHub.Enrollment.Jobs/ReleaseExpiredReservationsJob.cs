using System.Diagnostics;
using System.Security.Authentication;
using Arc4u.Dependency.Attribute;
using Arc4u.Diagnostics;
using Arc4u.OAuth2;
using Arc4u.Results;
using Arc4u.Security.Principal;
using Dev4u.CampusHub.Enrollment.UseCases.Enrollments;
using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Enrollment.Jobs;

/// <summary>
/// Every 5 minutes: gives back the seats of the enrollments whose saga died and rejects them as expired
/// (see <see cref="RecoverStalledEnrollments"/>). What fails is retried by the next run.
/// </summary>
[Export]
public class ReleaseExpiredReservationsJob(
            IServiceProvider scopedServiceProvider,
            IScopedServiceProviderAccessor accessor,
            IAppPrincipalFactory principalFactory,
            ILogger<ReleaseExpiredReservationsJob> logger,
            IActivitySourceFactory activitySourceFactory,
            IApplicationContext applicationContext,
            RecoverStalledEnrollments useCase)
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activitySource = activitySourceFactory.Get("CampusHub.Jobs");

            accessor.ServiceProvider = scopedServiceProvider;

            using var activity = activitySource?.StartActivity("ReleaseExpiredReservationsJob", ActivityKind.Producer);

            // Create a principal based on the service account: the calls made by the use case (Catalog) carry its token.
            await principalFactory.CreatePrincipalAsync("Service")
                .LogIfFailed()
                .OnFailedAsync(_ => throw new AuthenticationException("Failed to create principal for service account."))
                .OnSuccessNotNull(principal => { applicationContext.SetPrincipal(principal); })
                .ConfigureAwait(false);

            var report = await useCase.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            logger.Technical().LogMaintenanceReport("ReleaseExpiredReservationsJob", report.Examined, report.Handled, report.Skipped, report.Failed);

            logger.Technical().LogJobExecuted("ReleaseExpiredReservationsJob");
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
