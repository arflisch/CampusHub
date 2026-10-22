using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dev4u.CampusHub.ServiceDefaults;

public static class RetryingStartupTaskExtensions
{
    /// <summary>
    /// Runs an idempotent initialization (e.g. creating MongoDB indexes) in the background at startup, retrying
    /// with a capped exponential backoff until it succeeds. Startup is never blocked or crashed by a dependency
    /// that comes up after the service.
    /// </summary>
    public static IServiceCollection AddRetryingStartupTask(this IServiceCollection services, string name,
                                                            Func<IServiceProvider, CancellationToken, Task> task)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IHostedService>(sp => new RetryingStartupTask(name, task, sp,
            sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<ILogger<RetryingStartupTask>>()));
        return services;
    }
}

internal sealed partial class RetryingStartupTask(
    string name,
    Func<IServiceProvider, CancellationToken, Task> task,
    IServiceProvider services,
    TimeProvider timeProvider,
    ILogger<RetryingStartupTask> logger) : BackgroundService
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await task(services, stoppingToken).ConfigureAwait(false);
                LogSucceeded(logger, name);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailed(logger, ex, name, delay);
                await Task.Delay(delay, timeProvider, stoppingToken).ConfigureAwait(false);
                delay = delay * 2 > MaxDelay ? MaxDelay : delay * 2;
            }
        }
    }

    [LoggerMessage(EventId = 900, Level = LogLevel.Information, Message = "Startup task {TaskName} completed.")]
    private static partial void LogSucceeded(ILogger logger, string taskName);

    [LoggerMessage(EventId = 901, Level = LogLevel.Warning, Message = "Startup task {TaskName} failed, retrying in {Delay}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string taskName, TimeSpan delay);
}
