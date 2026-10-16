using XDrishti.Application.Abstractions.Live;

namespace XDrishti.Api.Live;

/// <summary>
/// Runs the hub's push loop (listener, price push, status, session check) for the lifetime of the API. The loop survives failures itself; this host only makes sure that nothing it throws can stop the API: <c>xd-api</c> stays healthy without the feed
/// and without the hub (ADR 0024 §7). Logs the type of a failure, never its text.
/// </summary>
internal sealed partial class LivePushService(ILivePushLoop loop, TimeProvider clock, ILogger<LivePushService> logger) : BackgroundService
{
    private static readonly TimeSpan RestartAfter = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await loop.RunAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // nothing the push does may stop the host
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Crashed(logger, ex.GetType().Name);
            }

            try
            {
                await Task.Delay(RestartAfter, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    [LoggerMessage(EventId = 4161, Level = LogLevel.Error, Message = "hub.loop_crashed type={Type}: restarting in 5 s")]
    private static partial void Crashed(ILogger logger, string type);
}
