using System.Buffers;
using System.Text;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
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

    /// <summary>The format string of the files this verb writes: <c>xd-sim-export-1</c> was the five files of P3-04b2; the cards and the extra manifest lines of P3-04c make it <c>xd-sim-export-2</c>, with or without cards; the legs of P4-01 (a <c>legs</c> column and members of the trade in run.json) make it <c>xd-sim-export-3</c>.</summary>
    public const string ExportFormat = "xd-sim-export-3";

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
        if (options.Exits is { Candidates.Count: > 1 })
        {
            return await SimulateCompare.RunAsync(options, root, services, engine, console, logger, cancellationToken);
        }

        var time = services.GetRequiredService<TimeProvider>();
        SimulateLog.Started(logger, options.StrategyId, options.Basket is null ? "symbols" : "basket", options.Symbols.Count, options.From, options.To);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var phaseStart = time.GetTimestamp();
        var gate = await SimulatePipeline.ResolveAsync(options.Basket, options.Symbols, provider, cancellationToken);
        if (gate.RefusalCode is { } refusal)
        {
            return await SimulatePipeline.RefuseAsync(console, logger, refusal, gate.Problems);
        }

        var universe = gate.Universe;
        var resolveTime = time.GetElapsedTime(phaseStart);

        phaseStart = time.GetTimestamp();
        var rules = SimulatePipeline.Rules(options.Fills);
        var request = SimulatePipeline.Request(options.StrategyId, options.Parameters, options.Timeframe, options.From, options.To, universe, engine, rules, options.Costs, options.AcknowledgeCsvAdjusted, options.Exits, options.StrategyVersion, options.StrategyHash);
        var ran = await SimulatePipeline.RunAsync(provider, request, cancellationToken);
        if (ran.IsFailure)
        {
            return await SimulatePipeline.RefuseAsync(console, logger, ran.Error.Code, SimulatePipeline.Problems(ran.Error));
        }

        var runTime = time.GetElapsedTime(phaseStart);

        phaseStart = time.GetTimestamp();
        var run = ran.Value;
        var labels = RunLabels.Of(run, rules);
        var runId = RunId.Of(run.Strategy.Id, run.From, run.To, run.RunKey);

        // selection, then the second read (a stale read refuses with nothing written), then render and write
        var choices = CardSelection.Choose(run, options.Cards);
        var cards = await SimulatePipeline.CardsAsync(provider, run, [.. choices.Select(c => c.Trade.Trade.Key)], cancellationToken);
        if (cards.IsFailure)
        {
            SimulatePipeline.LogStale(logger, cards.Error);
            return await SimulatePipeline.RefuseAsync(console, logger, cards.Error.Code, SimulatePipeline.Problems(cards.Error));
        }

        var writer = provider.GetService<ExportFolderWriter>() ?? new ExportFolderWriter();
        var extra = ExtraLines(run, options.Cards, choices);
        var written = await writer.WriteAsync(root, runId, Files(run, labels, runId, options.Cards, choices, cards.Value), entries => Manifest(run, entries, extra), options.Force, cancellationToken);
        if (written.IsFailure)
        {
            return await SimulatePipeline.RefuseAsync(console, logger, written.Error.Code, [written.Error.Message]);
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
            if (choices.Count > 0)
            {
                var cardBytes = outcome.Entries.Where(e => e.Name.StartsWith(CardNames.Folder + "/", StringComparison.Ordinal) || e.Name == CardNames.IndexName).Sum(e => e.Bytes);
                SimulateLog.CardsWritten(logger, choices.Count, cardBytes);
            }
        }

        if (await SimulatePipeline.StrategyLineAsync(provider, run.Strategy.DefinitionSha256, cancellationToken) is { } strategyLine)
        {
            await console.Out.WriteLineAsync(strategyLine);
        }

        await console.Out.WriteAsync(RunBanner.Text(labels));
        await console.Out.WriteAsync(SimulateSummary.Text(run, outcome, universe, phases, choices.Count, options.Cards, services.GetRequiredService<IConfiguration>()["Database:ConnectionString"]));
        if (run.Instruments.Count > 0 && run.Instruments.All(i => i.Skipped is not null))
        {
            await console.Error.WriteLineAsync("warning: every instrument was skipped; the skipped list above says why");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled after the export was written; it is complete in " + outcome.Folder);
            return Cancelled;
        }

        // the files are the audit copy and are already there; the store comes second, and its failure leaves them in place (spec P3-10, D12)
        return options.Store ? await SimulateStore.StoreAsync(run, outcome, provider, console, logger, cancellationToken) : Done;
    }

    private static List<ExportFile> Files(SimulationRun run, RunLabelSet labels, string runId, int cap, IReadOnlyList<CardChoice> choices, IReadOnlyList<TradeCardData> cards)
    {
        var files = new List<ExportFile>
        {
            new("trades.csv", sink => TradesCsv.Write(sink, run.RunKey, run.Trades)),
            new("open_at_end.csv", sink => TradesCsv.Write(sink, run.RunKey, run.OpenAtEnd)),
            new("run.json", sink => SimExportJson.Write(sink, run, labels, ExportFormat)),
            new("README.txt", sink => sink.Write(Encoding.UTF8.GetBytes(RunReadme.Text(labels, runId)))),
        };
        if (cap == 0)
        {
            return files;
        }

        files.Add(new ExportFile(CardNames.IndexName, sink => IndexHtml.Write(sink, run, labels, cap, choices)));
        for (var i = 0; i < choices.Count; i++)
        {
            var choice = choices[i];
            var data = cards[i];
            files.Add(new ExportFile(CardNames.Of(choice.Number, run.Trades.Count, choice.Trade.Trade), sink => TradeCardHtml.Write(sink, data, run, labels)));
        }

        return files;
    }

    /// <summary>The extra lines of the manifest (spec P3-04c, C6): the cap, the number of trades and each chosen trade with its stratum; with no cards only the first two.</summary>
    private static List<string> ExtraLines(SimulationRun run, int cap, IReadOnlyList<CardChoice> choices)
    {
        var lines = new List<string>
        {
            "cards.cap=" + cap.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "cards.trades=" + run.Trades.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        lines.AddRange(choices.Select(c => "cards.selected=" + c.Trade.Trade.Key + "|" + c.Stratum));
        return lines;
    }

    private static (byte[] Manifest, string ExportKey) Manifest(SimulationRun run, IReadOnlyList<ManifestEntry> entries, IReadOnlyList<string> extra) =>
        (ExportManifest.Build(ExportFormat, run.RunKey, run.Universe.BasketId, entries, extra), ExportKey.Of(ExportFormat, run.RunKey, run.Universe.BasketId, entries, extra));

    private static int Usage(SimulateConsole console, IReadOnlyList<string> errors) => SimulatePipeline.Usage(console, errors, SimulateOptions.Usage);

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
                    .Append(parameter.Default.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(parameter.HasRange ? ", range " + parameter.RangeText() : string.Empty).Append(": ").Append(parameter.Note).Append('\n');
            }
        }

        text.Append("exits (--exit POLICY:name=value,...; a|b lists candidate values and two candidates or more are compared on the same entries):\n");
        foreach (var line in ExitFlags.Catalog)
        {
            text.Append("  ").Append(line).Append('\n');
        }

        text.Append("  --time-stop min_progress_rr=<n>,intraday_bars=<n> (or swing_sessions=<n>): an overlay on every candidate\n");
        return text.ToString();
    }

    /// <summary>The exception type and its message cut to <see cref="MaxFailureMessage"/> characters, with control characters replaced; the stack goes to the log only. The verb reads no secret, so it has none to hide.</summary>
    internal static string Describe(Exception failure)
    {
        var message = string.Concat(failure.Message.Select(c => char.IsControl(c) ? ' ' : c));
        return failure.GetType().Name + ": " + (message.Length > MaxFailureMessage ? message[..MaxFailureMessage] + "..." : message);
    }
}
