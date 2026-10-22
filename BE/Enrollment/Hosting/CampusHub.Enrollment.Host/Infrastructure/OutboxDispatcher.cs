using Dev4u.CampusHub.Enrollment.Infrastructure.Enrollments;

namespace Dev4u.CampusHub.Enrollment.Host.Infrastructure;

/// <summary>
/// Drains the outbox every second. One DI scope per iteration (the processor and its collaborators are scoped),
/// and a failed iteration is logged instead of stopping the host.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes",
    Justification = "Instantiated by AddHostedService in Program.cs.")]
internal sealed partial class OutboxDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), timeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    var scope = scopeFactory.CreateAsyncScope();
                    await using var _ = scope.ConfigureAwait(false);
                    var processor = scope.ServiceProvider.GetRequiredService<IOutboxProcessor>();

                    // Keep draining while full batches come back, then wait for the next tick.
                    int published;
                    do
                    {
                        published = await processor.ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
                    }
                    while (published == OutboxProcessor.BatchSize);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogIterationFailed(logger, ex);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Error, Message = "Outbox dispatch iteration failed.")]
    private static partial void LogIterationFailed(ILogger logger, Exception exception);
}
