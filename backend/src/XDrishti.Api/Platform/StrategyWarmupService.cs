using XDrishti.Strategies.Compilation;

namespace XDrishti.Api.Platform;

/// <summary>
/// Warms up the rules compiler when the host starts (ISS-12, ADR 0034 amendment 2): the first compile of a process takes about 40 ms because the JIT turns about a thousand small methods into machine code on the way;
/// after <see cref="CompilerWarmup.Run"/> the first compile of ten conditions takes 3 to 5 ms. <see cref="StartAsync"/> starts the warm-up on a thread-pool thread and returns at once, so readiness is not delayed and
/// nothing depends on the warm-up; <see cref="StopAsync"/> does not wait for it. One line is logged when it ends, with the time it took (information), or, if it threw, a warning that carries the exception (nothing escapes: a
/// failed warm-up only means the first compile is slower). The work is a delegate so that a test can gate a fake; the registration passes <see cref="CompilerWarmup.Run"/>. Never logs a strategy text.
/// </summary>
internal sealed partial class StrategyWarmupService(TimeProvider clock, ILogger<StrategyWarmupService> logger, Action warmUp) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(Warm, CancellationToken.None);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Warm()
    {
        var started = clock.GetTimestamp();
        try
        {
            warmUp();
            var elapsed = clock.GetElapsedTime(started).TotalMilliseconds;
            Finished(logger, LogLevel.Information, null, elapsed);
        }
#pragma warning disable CA1031 // a failed warm-up must not reach anyone: the compiler only starts slower
        catch (Exception ex)
#pragma warning restore CA1031
        {
            var elapsed = clock.GetElapsedTime(started).TotalMilliseconds;
            Finished(logger, LogLevel.Warning, ex, elapsed);
        }
    }

    [LoggerMessage(EventId = 4401, Message = "compiler.warm_up elapsed_ms={ElapsedMs}")]
    private static partial void Finished(ILogger logger, LogLevel level, Exception? exception, double elapsedMs);
}
