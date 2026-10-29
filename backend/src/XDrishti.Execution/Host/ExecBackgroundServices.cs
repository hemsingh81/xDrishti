using Microsoft.Extensions.Options;
using XDrishti.Execution.Application;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Host;

/// <summary>
/// The heartbeat and the settings read, every 10 seconds, whatever the environment. The state is always <c>recovering</c> in this slice (entries are blocked until recovery exists, P12-06). It builds no broker client; a failure is logged (counts only) and tried again.
/// </summary>
internal sealed partial class ExecHeartbeatService(IOptions<ExecOptions> options, ExecSettingsReader settings, IExecHeartbeats heartbeats, ExecCounters counters, ILogger<ExecHeartbeatService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var environment = options.Value.Parsed;
        LogBoot(ExecBootService.BootState, environment);
        string lastReplaced = string.Empty;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        do
        {
            try
            {
                var view = await settings.ReadAsync(stoppingToken);
                var replaced = string.Join(',', view.Replaced);
                if (replaced != lastReplaced)
                {
                    lastReplaced = replaced;
                    LogReplaced(view.Replaced.Count, replaced);
                }

                await heartbeats.BeatAsync(new ExecHeartbeat(ExecBootService.BootState, environment.ToString().ToLowerInvariant(), view.Hash(), counters.Answered), stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 6601, Level = LogLevel.Warning, Message = "{Count} execution setting(s) were outside their range or unreadable and use the default: {Keys}")]
    private partial void LogReplaced(int count, string keys);

    [LoggerMessage(EventId = 6604, Level = LogLevel.Warning, Message = "The execution heartbeat or settings read failed ({Kind}); trying again")]
    private partial void LogFailed(string kind);

    [LoggerMessage(EventId = 6600, Level = LogLevel.Information, Message = "xd-exec started: state {State}, environment {Environment}; no broker client is built")]
    private partial void LogBoot(string state, ExecEnvironment environment);
}

/// <summary>The command poller: every 500 ms it answers the commands that have no answer (see <see cref="ExecCommandProcessor"/>). It reads and answers rows only; no kind reaches a broker.</summary>
internal sealed partial class ExecCommandPoller(ExecCommandProcessor processor, ExecCounters counters, ILogger<ExecCommandPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        do
        {
            try
            {
                var answered = await processor.RunOnceAsync(stoppingToken);
                if (answered > 0)
                {
                    counters.Add(answered);
                    LogAnswered(answered);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LogFailed(e.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(EventId = 6602, Level = LogLevel.Information, Message = "Answered {Count} execution command(s)")]
    private partial void LogAnswered(int count);

    [LoggerMessage(EventId = 6605, Level = LogLevel.Warning, Message = "The command poll failed ({Kind}); trying again")]
    private partial void LogFailed(string kind);
}

/// <summary>How many commands this process answered (shown in the heartbeat).</summary>
internal sealed class ExecCounters
{
    private int _answered;

    public int Answered => Volatile.Read(ref _answered);

    public void Add(int n) => Interlocked.Add(ref _answered, n);
}
