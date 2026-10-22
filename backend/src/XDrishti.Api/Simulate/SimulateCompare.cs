using System.Buffers;
using System.Globalization;
using System.Text;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Research;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Text;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The comparison step of the <c>simulate</c> verb (spec P4-05, D1 to D8, D11): two exit candidates or more run on the same entries by the comparison handler, and the folder holds <c>comparison.csv</c>, <c>diagnostics.csv</c>, the trades and the
/// run record of each candidate (the first candidate's keep the names <c>trades.csv</c>, <c>open_at_end.csv</c> and <c>run.json</c>), and the readme. The folder is named from the comparison key, so it never meets the folder of a plain run. No trade card
/// is written: a card shows one run, and a single <c>--exit</c> gives the cards of one policy. The console prints the banner, the summary of the first candidate's run and the two tables.
/// </summary>
internal static class SimulateCompare
{
    /// <summary>The format string of a comparison folder: the files of <c>xd-sim-export-3</c> plus the comparison and one pair of trade files and one run record for each candidate.</summary>
    public const string ExportFormat = "xd-sim-export-4";

    /// <summary>The names of the files of one candidate: the first keeps the plain names; the others carry the index and a cleaned id.</summary>
    public static (string Trades, string Open, string Run) Names(int index, string id)
    {
        if (index == 0)
        {
            return ("trades.csv", "open_at_end.csv", "run.json");
        }

        var slug = string.Concat(id.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_'));
        slug = (index + 1).ToString("00", CultureInfo.InvariantCulture) + "-" + (slug.Length > 50 ? slug[..50] : slug);
        return ("trades-" + slug + ".csv", "open_at_end-" + slug + ".csv", "run-" + slug + ".json");
    }

    public static async Task<int> RunAsync(SimulateOptions options, string root, IServiceProvider services, ISimulationEngine engine, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
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
        var ran = await SimulatePipeline.RunComparisonAsync(provider, request, cancellationToken);
        if (ran.IsFailure)
        {
            return await SimulatePipeline.RefuseAsync(console, logger, ran.Error.Code, SimulatePipeline.Problems(ran.Error));
        }

        var runTime = time.GetElapsedTime(phaseStart);

        phaseStart = time.GetTimestamp();
        var comparison = ran.Value;
        var first = comparison.Candidates[0].Run;
        var labels = RunLabels.Of(first, rules);
        ExitSelection? selection = null;
        string? selectionKey = null;
        if (options.Selection is { } selectionOptions)
        {
            var calendar = await provider.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
            selection = ExitSelector.Select(SimulateSelect.InputOf(comparison, calendar, selectionOptions));
            selectionKey = ExitSelectionKey.Of(comparison.ComparisonKey, selection);
        }

        var runId = RunId.Of(first.Strategy.Id, first.From, first.To, selectionKey ?? comparison.ComparisonKey);
        var writer = provider.GetService<ExportFolderWriter>() ?? new ExportFolderWriter();
        var swing = !comparison.Report.GateApplied;
        var readme = Readme(comparison, labels, runId, swing, selection);
        var written = await writer.WriteAsync(root, runId, Files(comparison, labels, readme, selection), entries => Manifest(comparison, entries, selectionKey), options.Force, cancellationToken);
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
        }

        await console.Out.WriteAsync(RunBanner.Text(labels));
        foreach (var line in ComparisonText.BannerLines(swing))
        {
            await console.Out.WriteLineAsync(line);
        }

        await console.Out.WriteAsync(SimulateSummary.Text(first, outcome, universe, phases, 0, 0, services.GetRequiredService<IConfiguration>()["Database:ConnectionString"]));
        await console.Out.WriteLineAsync("comparison key: " + comparison.ComparisonKey);
        await console.Out.WriteLineAsync("entries key: " + comparison.EntriesKey);
        foreach (var candidate in comparison.Candidates)
        {
            await console.Out.WriteLineAsync("candidate " + candidate.Id + ": run key " + candidate.Run.RunKey);
        }

        await console.Out.WriteAsync(ComparisonText.Table(comparison.Report));
        await console.Out.WriteAsync(ComparisonText.Diagnostics(comparison.Report));
        if (selection is not null)
        {
            await console.Out.WriteLineAsync("exit selection key: " + selectionKey);
            await console.Out.WriteAsync(ExitSelectionText.Summary(selection));
        }

        if (first.Instruments.Count > 0 && first.Instruments.All(i => i.Skipped is not null))
        {
            await console.Error.WriteLineAsync("warning: every instrument was skipped; the skipped list above says why");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled after the export was written; it is complete in " + outcome.Folder);
            return SimulateCommand.Cancelled;
        }

        return options.Store ? await SimulateStore.StoreComparisonAsync(comparison, outcome, provider, console, logger, cancellationToken, selection) : SimulateCommand.Done;
    }

    private static List<ExportFile> Files(ComparisonRun comparison, RunLabelSet labels, string readme, ExitSelection? selection)
    {
        var files = new List<ExportFile>
        {
            new("comparison.csv", sink => ComparisonCsv.Write(sink, comparison.Report)),
            new("diagnostics.csv", sink => ComparisonCsv.WriteDiagnostics(sink, comparison.Report)),
            new("README.txt", sink => sink.Write(Encoding.UTF8.GetBytes(readme))),
        };
        if (selection is not null)
        {
            files.Add(new ExportFile("exit-selection.json", sink => ExitSelectionJson.Write(sink, selection, ExportFormat)));
            files.Add(new ExportFile("exit-selection.csv", sink => ExitSelectionCsv.Write(sink, selection)));
        }

        for (var i = 0; i < comparison.Candidates.Count; i++)
        {
            var candidate = comparison.Candidates[i];
            var run = candidate.Run;
            var names = Names(i, candidate.Id);
            files.Add(new ExportFile(names.Trades, sink => TradesCsv.Write(sink, run.RunKey, run.Trades)));
            files.Add(new ExportFile(names.Open, sink => TradesCsv.Write(sink, run.RunKey, run.OpenAtEnd)));
            files.Add(new ExportFile(names.Run, sink => SimExportJson.Write(sink, run, labels, ExportFormat)));
        }

        return files;
    }

    private static string Readme(ComparisonRun comparison, RunLabelSet labels, string runId, bool swing, ExitSelection? selection)
    {
        var text = new StringBuilder();
        text.Append(RunReadme.Text(labels, runId));
        text.Append("\nComparison of ").Append(comparison.Candidates.Count.ToString(CultureInfo.InvariantCulture)).Append(" exit candidates on the same entries (spec P4-05)\n");
        foreach (var line in ComparisonText.BannerLines(swing))
        {
            text.Append(line).Append('\n');
        }

        text.Append("comparison key: ").Append(comparison.ComparisonKey).Append('\n');
        text.Append("entries key: ").Append(comparison.EntriesKey).Append('\n');
        for (var i = 0; i < comparison.Candidates.Count; i++)
        {
            var names = Names(i, comparison.Candidates[i].Id);
            text.Append("candidate ").Append(comparison.Candidates[i].Id).Append(": run key ").Append(comparison.Candidates[i].Run.RunKey).Append(", files ").Append(names.Trades).Append(", ").Append(names.Open).Append(", ").Append(names.Run).Append('\n');
        }

        text.Append("No trade card is written for a comparison: a card shows one run; run the candidate you want to read alone with a single --exit.\n\n");
        text.Append(ComparisonText.Table(comparison.Report)).Append('\n').Append(ComparisonText.Diagnostics(comparison.Report));
        if (selection is not null)
        {
            text.Append('\n').Append(ExitSelectionText.Summary(selection));
        }

        return text.ToString();
    }

    private static (byte[] Manifest, string ExportKey) Manifest(ComparisonRun comparison, IReadOnlyList<ManifestEntry> entries, string? selectionKey)
    {
        var extra = new List<string> { "comparison.entries_key=" + comparison.EntriesKey, "comparison.candidates=" + comparison.Candidates.Count.ToString(CultureInfo.InvariantCulture) };
        if (selectionKey is not null)
        {
            extra.Add("selection.key=" + selectionKey);
        }

        extra.AddRange(comparison.Candidates.Select(c => "comparison.member=" + c.Run.RunKey + "|" + c.Id));
        var basket = comparison.Candidates[0].Run.Universe.BasketId;
        return (ExportManifest.Build(ExportFormat, comparison.ComparisonKey, basket, entries, extra), ExportKey.Of(ExportFormat, comparison.ComparisonKey, basket, entries, extra));
    }
}
