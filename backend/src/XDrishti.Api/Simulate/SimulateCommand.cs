using System.Buffers;
using System.Text;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Text;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The <c>simulate</c> verb (spec P3-04b2, D1, D5): runs a built-in strategy over a few instruments or a basket from the stored bars and writes the files of the export library. It holds the order only (parse, check the configuration, resolve the
/// universe, gate the tick unit, run the handler, label, render and write, print) and no decision that belongs to the Domain (the gate, the labels) or to the library. Returns the process exit code: 0 done, 1 refused with nothing written, 2 the
/// command line or the configuration is wrong, 3 an unexpected failure, 130 cancelled. It is a host step that calls handlers, on the precedent of <c>OwnerBootstrap</c> (ADR 0041).
/// </summary>
internal static class SimulateCommand
{
    public const int Done = 0;

    public const int Refused = 1;

    public const int UsageError = 2;

    public const int Failed = 3;

    public const int Cancelled = 130;

    /// <summary>The format string of the files this verb writes; the cards of P3-04c change it to <c>xd-sim-export-2</c>.</summary>
    public const string ExportFormat = "xd-sim-export-1";

    /// <summary>The longest exception message printed on an unexpected failure.</summary>
    public const int MaxFailureMessage = 300;

    /// <summary>The most problems printed for a refusal.</summary>
    public const int MaxProblems = SimulationErrors.MaxStrategyProblems;

    /// <param name="args">The arguments after the verb name.</param>
    /// <param name="services">The services of <see cref="SimulateHost"/>.</param>
    /// <param name="console">Where the text goes.</param>
    /// <param name="exportRoot">The export root (<c>Export:Directory</c>); checked here.</param>
    /// <param name="cancellationToken">Ctrl-C.</param>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, IServiceProvider services, SimulateConsole console, string? exportRoot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);

        var engine = services.GetRequiredService<ISimulationEngine>();
        var parsed = SimulateOptions.Parse(args, [.. engine.BuiltIns.Select(b => b.Id)]);
        if (parsed.Options is not { } options)
        {
            return Usage(console, parsed.Errors);
        }

        if (options.ListStrategies)
        {
            console.Out.Write(Catalog(engine.BuiltIns));
            return Done;
        }

        var root = ExportPaths.Root(exportRoot);
        if (root.IsFailure)
        {
            return Usage(console, [root.Error.Message]);
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SimulateCommand).FullName!);
        try
        {
            return await ExecuteAsync(options, root.Value, services, engine, console, logger, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled; nothing was written (a partial export is removed)");
            return Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            SimulateLog.Failed(logger, failure.GetType().Name);
            await console.Error.WriteLineAsync("failed: " + Describe(failure));
            return Failed;
        }
    }

    private static async Task<int> ExecuteAsync(
        SimulateOptions options, string root, IServiceProvider services, ISimulationEngine engine, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        var time = services.GetRequiredService<TimeProvider>();
        SimulateLog.Started(logger, options.StrategyId, options.Basket is null ? "symbols" : "basket", options.Symbols.Count, options.From, options.To);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var phaseStart = time.GetTimestamp();
        var universe = await UniverseResolver.ResolveAsync(options, provider, cancellationToken);
        if (universe.IsRefused)
        {
            return await RefuseAsync(console, logger, "simulate.universe", universe.Problems);
        }

        if (TickUnitCheck.Suspect(universe.Probes) is { Count: > 0 } suspects)
        {
            return await RefuseAsync(console, logger, "simulate.tick_unit", TickProblems(suspects));
        }

        var resolveTime = time.GetElapsedTime(phaseStart);

        phaseStart = time.GetTimestamp();
        var rules = Rules(options.Fills);
        var request = Request(options, universe, engine, rules);
        var handler = provider.GetRequiredService<IQueryHandler<RunSimulationQuery, Result<SimulationRun>>>();
        var ran = await handler.HandleAsync(new RunSimulationQuery(request), cancellationToken);
        if (ran.IsFailure)
        {
            return await RefuseAsync(console, logger, ran.Error.Code, Problems(ran.Error));
        }

        var runTime = time.GetElapsedTime(phaseStart);

        phaseStart = time.GetTimestamp();
        var run = ran.Value;
        var labels = RunLabels.Of(run, rules);
        var runId = RunId.Of(run.Strategy.Id, run.From, run.To, run.RunKey);
        var writer = provider.GetService<ExportFolderWriter>() ?? new ExportFolderWriter();
        var written = await writer.WriteAsync(root, runId, Files(run, labels, runId), entries => Manifest(run, entries), options.Force, cancellationToken);
        if (written.IsFailure)
        {
            return await RefuseAsync(console, logger, written.Error.Code, [written.Error.Message]);
        }

        var outcome = written.Value;
        var phases = new SimulatePhases(resolveTime, runTime, time.GetElapsedTime(phaseStart));
        if (outcome.Status == ExportStatus.Unchanged)
        {
            SimulateLog.Unchanged(logger, runId);
        }
        else
        {
            var bytes = outcome.Entries.Sum(e => e.Bytes);
            SimulateLog.Written(logger, runId, outcome.Entries.Count + 1, bytes);
        }

        await console.Out.WriteAsync(RunBanner.Text(labels));
        await console.Out.WriteAsync(SimulateSummary.Text(run, outcome, universe, phases, services.GetRequiredService<IConfiguration>()["Database:ConnectionString"]));
        if (run.Instruments.Count > 0 && run.Instruments.All(i => i.Skipped is not null))
        {
            await console.Error.WriteLineAsync("warning: every instrument was skipped; the skipped list above says why");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled after the export was written; it is complete in " + outcome.Folder);
            return Cancelled;
        }

        return Done;
    }

    private static FillRules Rules(FillsMode mode) =>
        mode == FillsMode.NoSlippage ? FillRules.Create(FillRules.Default.EntryLimitPercent, SlippageConfig.None).Value : FillRules.Default;

    private static SimulationRequest Request(SimulateOptions options, SimulateUniverse universe, ISimulationEngine engine, FillRules rules)
    {
        var timeframe = options.Timeframe
            ?? engine.BuiltIns.FirstOrDefault(b => string.Equals(b.Id, options.StrategyId, StringComparison.Ordinal))?.DefaultTimeframe.ToCode()
            ?? Timeframe.M15.ToCode();
        var fills = new FillRulesInput(rules.EntryLimitPercent, rules.Slippage.Ticks, rules.Slippage.BarRangeFrac, rules.Slippage.StopExitExtraTicks);
        return new SimulationRequest(options.StrategyId, options.Parameters, timeframe, options.From, options.To, universe.InstrumentIds, universe.BasketId, options.AcknowledgeCsvAdjusted, null, fills);
    }

    private static List<ExportFile> Files(SimulationRun run, RunLabelSet labels, string runId) =>
    [
        new("trades.csv", sink => TradesCsv.Write(sink, run.RunKey, run.Trades)),
        new("open_at_end.csv", sink => TradesCsv.Write(sink, run.RunKey, run.OpenAtEnd)),
        new("run.json", sink => SimExportJson.Write(sink, run, labels, ExportFormat)),
        new("README.txt", sink => sink.Write(Encoding.UTF8.GetBytes(RunReadme.Text(labels, runId)))),
    ];

    private static (byte[] Manifest, string ExportKey) Manifest(SimulationRun run, IReadOnlyList<ManifestEntry> entries) =>
        (ExportManifest.Build(ExportFormat, run.RunKey, run.Universe.BasketId, entries, []), ExportKey.Of(ExportFormat, run.RunKey, run.Universe.BasketId, entries, []));

    private static int Usage(SimulateConsole console, IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
        {
            console.Error.WriteLine(error);
        }

        console.Error.Write(SimulateOptions.Usage);
        return UsageError;
    }

    private static async Task<int> RefuseAsync(SimulateConsole console, ILogger logger, string code, IReadOnlyList<string> problems)
    {
        SimulateLog.Refused(logger, code, problems.Count);
        foreach (var line in problems.Take(MaxProblems))
        {
            await console.Error.WriteLineAsync(line);
        }

        if (problems.Count > MaxProblems)
        {
            await console.Error.WriteLineAsync("and " + (problems.Count - MaxProblems).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more problems");
        }

        return Refused;
    }

    /// <summary>One <c>path: code: message</c> per problem of a refused run, at most <see cref="MaxProblems"/>; a refusal without fields is one <c>code: message</c> line.</summary>
    private static List<string> Problems(Error error) =>
        error.Fields is { Count: > 0 } fields
            ? [.. fields.Select(f => f.Field + ": " + (f.Code ?? error.Code) + ": " + f.Message)]
            : [error.Code + ": " + error.Message];

    private static List<string> TickProblems(IReadOnlyList<(TickProbe Probe, decimal ConvertedRupees)> suspects)
    {
        var lines = new List<string>
        {
            "simulate.tick_unit: " + suspects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " share or ETF rows have a stored tick that reads as less than one paisa after the paise conversion; the master table may hold rupees (ADR 0038 item 6). Nothing was read.",
        };
        lines.AddRange(suspects.Take(3).Select(s =>
            "  " + SimulateOptions.Show(s.Probe.Symbol) + ": stored " + SimulateSummary.Rupees(s.Probe.StoredTick!.Value)
            + ", converted " + SimulateSummary.Rupees(s.ConvertedRupees) + " rupee"));
        return lines;
    }

    private static string Catalog(IReadOnlyList<BuiltInStrategyInfo> catalog)
    {
        var text = new StringBuilder();
        foreach (var entry in catalog)
        {
            text.Append(entry.Id).Append(": ").Append(entry.Description).Append('\n');
            text.Append("  timeframes: ").Append(string.Join(", ", entry.Timeframes.Select(t => t.ToCode() + (t == entry.DefaultTimeframe ? " (default)" : string.Empty)))).Append('\n');
            foreach (var parameter in entry.Parameters)
            {
                text.Append("  --param ").Append(parameter.Name).Append("=<number>  default ")
                    .Append(parameter.Default.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(": ").Append(parameter.Note).Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>The exception type and its message cut to <see cref="MaxFailureMessage"/> characters, with control characters replaced; the stack goes to the log only. The verb reads no secret, so it has none to hide.</summary>
    private static string Describe(Exception failure)
    {
        var message = string.Concat(failure.Message.Select(c => char.IsControl(c) ? ' ' : c));
        return failure.GetType().Name + ": " + (message.Length > MaxFailureMessage ? message[..MaxFailureMessage] + "..." : message);
    }
}
