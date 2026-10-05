using DeBliss.Application.Notifications.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeBliss.Infrastructure.Notifications;

/// <summary>
/// The lightweight background loop that drives reminder sending. Every tick it
/// opens one DI scope (mirroring a web request) and asks the dispatch service
/// to process the due rows — claiming, sending through the provider, and
/// persisting each result. A failing tick is logged and never crashes the
/// host; the interval is configurable via 'Reminders:DispatchIntervalSeconds'
/// and the loop starts only after a short delay so application startup is
/// never blocked or raced by dispatch work.
/// </summary>
public sealed class ReminderDispatchWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<ReminderDispatchWorker> logger) : BackgroundService
{
    public const int DefaultIntervalSeconds = 60;

    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var interval = ResolveInterval();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The reminder dispatch cycle failed; it will run again next interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task DispatchOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IReminderDispatchService>();

        var result = await dispatcher.ProcessDueAsync(cancellationToken);

        if (result.Sent > 0 || result.Failed > 0 || result.Cancelled > 0)
        {
            logger.LogInformation(
                "Reminder dispatch cycle: {Sent} sent, {Failed} failed, {Skipped} skipped, {Cancelled} cancelled, {Deferred} deferred.",
                result.Sent,
                result.Failed,
                result.Skipped,
                result.Cancelled,
                result.Deferred);
        }
    }

    private TimeSpan ResolveInterval()
    {
        var raw = configuration["Reminders:DispatchIntervalSeconds"];

        return int.TryParse(raw, out var seconds) && seconds >= 5
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.FromSeconds(DefaultIntervalSeconds);
    }
}
