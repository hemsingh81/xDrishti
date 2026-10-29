using Microsoft.Extensions.Options;

namespace XDrishti.Execution.Host;

/// <summary>Always <c>recovering</c> in this slice: entries are blocked until recovery exists (P12-06). The service builds no broker client.</summary>
internal sealed partial class ExecBootService(IOptions<ExecOptions> options, ILogger<ExecBootService> logger) : BackgroundService
{
    public const string BootState = "recovering";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var environment = options.Value.Parsed;
        LogBoot(BootState, environment);
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 6600, Level = LogLevel.Information, Message = "xd-exec started: state {State}, environment {Environment}; no broker client is built")]
    private partial void LogBoot(string state, Domain.ExecEnvironment environment);
}
