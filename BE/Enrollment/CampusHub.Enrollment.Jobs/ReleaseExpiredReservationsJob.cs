using System.Diagnostics;
using System.Security.Authentication;
using Arc4u.Dependency.Attribute;
using Arc4u.Diagnostics;
using Arc4u.OAuth2;
using Arc4u.Results;
using Arc4u.Security.Principal;
using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.Enrollment.Jobs;

[Export]
public class ReleaseExpiredReservationsJob(
            IServiceProvider scopedServiceProvider,
            IScopedServiceProviderAccessor accessor,
            IAppPrincipalFactory principalFactory,
            ILogger<ReleaseExpiredReservationsJob> logger,
            IActivitySourceFactory activitySourceFactory,
            IApplicationContext applicationContext)
{
    public async Task ExecuteAsync()
    {
        try
        {
            var activitySource = activitySourceFactory.Get("CampusHub.Jobs");

            accessor.ServiceProvider = scopedServiceProvider;

            using var activity = activitySource?.StartActivity("ReleaseExpiredReservationsJob", ActivityKind.Producer);

            // Create a principal based on the service account.
            await principalFactory.CreatePrincipalAsync("Service")
                .LogIfFailed()
                .OnFailedAsync(_ => throw new AuthenticationException("Failed to create principal for service account."))
                .OnSuccessNotNull(principal => { applicationContext.SetPrincipal(principal); })
                .ConfigureAwait(false);

            // Call code here in the context of the service account name...

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
