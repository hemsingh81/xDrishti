using XDrishti.Api.Simulate.Assist;
using XDrishti.Api.Simulate.Gate;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.SimExport.Verification;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The <c>--assist</c> mode of <c>verify-pack</c> (spec P3-09c): wires the real ports (the stored bars of the database, Dhan's chart endpoint or the folder the owner names) to <see cref="AssistRunner"/> and prints its lines. The pack folder is checked first like the check mode does
/// (no link, the manifest verified for every file but <c>check-log.csv</c> and <c>engine-values.csv</c>, which is never opened before the log is written; the runner verifies it afterwards). Exit 0 when the files are written, 2 for an input that cannot be used, 130 cancelled.
/// </summary>
internal static class VerifyPackAssist
{
    public static async Task<int> RunAsync(VerifyPackOptions options, IServiceProvider services, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        if (options.SecondSourceFolder is null && string.IsNullOrEmpty(services.GetRequiredService<IConfiguration>()["BrokerVault:Key"]))
        {
            return await Refuse(console, "the Dhan second source needs the broker vault key (BrokerVault__Key, set by scripts/verify-pack.sh from the Keychain); or use --second-source-folder");
        }

        var gate = options.GatePath is not null;
        var folder = Path.GetFullPath(options.GatePath ?? options.AssistPath!);
        if (!Directory.Exists(folder) || ExportPaths.IsLink(folder))
        {
            return await Refuse(console, "the pack folder does not exist or is a symbolic link");
        }

        if (VerifyPackCheck.VerifyManifest(folder, PackFiles.EngineValuesName) is { } changed)
        {
            return await Refuse(console, changed);
        }

        if (options.SecondSourceFolder is { } offline && !Directory.Exists(offline))
        {
            return await Refuse(console, "the second-source folder does not exist");
        }

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        ISecondSource source = options.SecondSourceFolder is { } dir
            ? new FolderSecondSource(Path.GetFullPath(dir))
            : new DhanSecondSource(provider.GetRequiredService<IFetchTargets>(), provider.GetRequiredService<IIntradayBarSource>());
        var stored = new StoredDays(
            provider.GetRequiredService<IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>>>(),
            provider.GetRequiredService<IQueryHandler<GetInstrumentQuery, Result<InstrumentDetail>>>(),
            provider.GetRequiredService<IBarStore>());

        if (gate)
        {
            var decided = await GateRunner.RunAsync(new DiskPackFolder(folder), source, stored, cancellationToken);
            foreach (var line in decided.Lines)
            {
                await (decided.Status is null ? console.Error : console.Out).WriteLineAsync(line);
            }

            if (decided.Status is { } status)
            {
                var code = GateStatuses.Code(status);
                SimulateLog.GateDecided(logger, code, source.Name);
            }

            return decided.Exit;
        }

        var outcome = await AssistRunner.RunAsync(new DiskPackFolder(folder), source, stored, cancellationToken);
        foreach (var line in outcome.Lines)
        {
            await (outcome.Exit == SimulateCommand.Done ? console.Out : console.Error).WriteLineAsync(line);
        }

        if (outcome.Exit == SimulateCommand.Done)
        {
            var agree = outcome.Lines.FirstOrDefault(l => l.StartsWith(Verdicts.AgreeAll + ": ", StringComparison.Ordinal));
            var agreeCount = agree is null ? 0 : int.Parse(agree[(Verdicts.AgreeAll.Length + 2)..], System.Globalization.CultureInfo.InvariantCulture);
            var total = outcome.Lines.Where(l => Verdicts.All.Any(v => l.StartsWith(v + ": ", StringComparison.Ordinal))).Sum(l => int.Parse(l[(l.IndexOf(": ", StringComparison.Ordinal) + 2)..], System.Globalization.CultureInfo.InvariantCulture));
            SimulateLog.AssistRead(logger, total, source.Name, agreeCount, total - agreeCount);
        }

        return outcome.Exit;
    }

    private static async Task<int> Refuse(SimulateConsole console, string message)
    {
        await console.Error.WriteLineAsync(message);
        return SimulateCommand.UsageError;
    }
}
