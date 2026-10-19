using System.Buffers;
using System.Globalization;
using System.Text;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Manifest;
using XDrishti.SimExport.Text;
using XDrishti.SimExport.Verification;

namespace XDrishti.Api.Simulate;

/// <summary>
/// The <c>verify-pack</c> verb (spec P3-09, V4): runs each named built-in strategy with its registered defaults through the pipeline <c>simulate</c> uses, draws the trades of the pack from the pooled runs with the owner's seed, reads the cards of
/// the chosen trades a second time (one read per run), and writes the pack folder. It holds the order only; the draw, the categories and the files are the library's. It never writes to the database and never uses the result store.
/// Returns the process exit code of <see cref="SimulateCommand"/>: 0 done, 1 refused with nothing written, 2 the command line or configuration is wrong, 3 an unexpected failure, 130 cancelled.
/// </summary>
internal static class VerifyPackCommand
{
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
        var parsed = VerifyPackOptions.Parse(args, [.. engine.BuiltIns.Select(b => b.Id)]);
        if (parsed.Options is not { } options)
        {
            return SimulatePipeline.Usage(console, parsed.Errors, VerifyPackOptions.Usage);
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(VerifyPackCommand).FullName!);
        if (options.CheckPath is not null)
        {
            await console.Error.WriteLineAsync("the check mode is not in this build (P3-09 part B)");
            return SimulateCommand.UsageError;
        }

        var root = ExportPaths.Root(exportRoot);
        if (root.IsFailure)
        {
            return SimulatePipeline.Usage(console, [root.Error.Message], VerifyPackOptions.Usage);
        }

        try
        {
            return await ExecuteAsync(options, root.Value, services, engine, console, logger, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled; nothing was written (a partial pack is removed)");
            return SimulateCommand.Cancelled;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            SimulateLog.Failed(logger, failure.GetType().Name);
            await console.Error.WriteLineAsync("failed: " + SimulateCommand.Describe(failure));
            return SimulateCommand.Failed;
        }
    }

    private static async Task<int> ExecuteAsync(
        VerifyPackOptions options, string root, IServiceProvider services, ISimulationEngine engine, SimulateConsole console, ILogger logger, CancellationToken cancellationToken)
    {
        var time = services.GetRequiredService<TimeProvider>();
        SimulateLog.PackStarted(logger, options.Strategies.Count, options.Basket is null ? "symbols" : "basket", options.Symbols.Count, options.From, options.To, options.Count);
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var phaseStart = time.GetTimestamp();
        var gate = await SimulatePipeline.ResolveAsync(options.Basket, options.Symbols, provider, cancellationToken);
        if (gate.RefusalCode is { } refusal)
        {
            return await SimulatePipeline.RefuseAsync(console, logger, refusal, gate.Problems);
        }

        var resolveTime = time.GetElapsedTime(phaseStart);

        phaseStart = time.GetTimestamp();
        var rules = SimulatePipeline.Rules(FillsMode.Modelled);
        var runs = new List<SimulationRun>(options.Strategies.Count);
        foreach (var strategy in options.Strategies)
        {
            // the registered defaults and the timeframe of the built-in: a pack checks the rule set as it ships
            var request = SimulatePipeline.Request(strategy, new Dictionary<string, double>(), null, options.From, options.To, gate.Universe, engine, rules, options.Costs, options.AcknowledgeCsvAdjusted);
            var ran = await SimulatePipeline.RunAsync(provider, request, cancellationToken);
            if (ran.IsFailure)
            {
                return await SimulatePipeline.RefuseAsync(console, logger, ran.Error.Code, SimulatePipeline.Problems(ran.Error));
            }

            runs.Add(ran.Value);
        }

        var runTime = time.GetElapsedTime(phaseStart);

        phaseStart = time.GetTimestamp();
        var packRuns = runs.Select(r => new PackRun(r, RunLabels.Of(r, rules))).ToList();
        var plan = PackPlanner.Plan(runs, options.Seed, options.Count);
        var basketId = runs[0].Universe.BasketId;
        var packKey = PackFiles.PackKey(options.Seed, options.Count, basketId, runs);
        var packId = PackFiles.PackId(options.Seed, packKey);

        var cards = new Dictionary<int, TradeCardData>();
        for (var r = 0; r < runs.Count; r++)
        {
            var rows = plan.Rows.Where(row => row.RunIndex == r).ToList();
            var read = await SimulatePipeline.CardsAsync(provider, runs[r], [.. rows.Select(row => row.Trade.Trade.Key)], cancellationToken);
            if (read.IsFailure)
            {
                SimulatePipeline.LogStale(logger, read.Error);
                return await SimulatePipeline.RefuseAsync(console, logger, read.Error.Code, SimulatePipeline.Problems(read.Error));
            }

            for (var i = 0; i < rows.Count; i++)
            {
                cards[rows[i].Seq] = read.Value[i];
            }
        }

        var files = Files(options.Seed, options.Count, packId, packKey, basketId, packRuns, plan, cards);
        var extra = PackFiles.ExtraLines(options.Seed, options.Count, runs, plan);
        var blankLog = Render(sink => PackFiles.WriteCheckLog(sink, plan));
        await WarnOnForceAsync(options.Force, Path.Combine(root, PackFiles.Folder, packId), blankLog, console);

        var writer = provider.GetService<ExportFolderWriter>() ?? new ExportFolderWriter();
        var written = await writer.WriteAsync(
            root,
            packId,
            files,
            entries => (ExportManifest.Build(PackFiles.Format, packKey, basketId, entries, extra), ExportKey.Of(PackFiles.Format, packKey, basketId, entries, extra)),
            options.Force,
            cancellationToken,
            PackFiles.Folder);
        if (written.IsFailure)
        {
            var problems = new List<string> { written.Error.Message };
            if (written.Error.Code == ExportErrors.ExistsDifferentCode)
            {
                problems.AddRange(ChangedFiles(Path.Combine(root, PackFiles.Folder, packId)));
            }

            return await SimulatePipeline.RefuseAsync(console, logger, written.Error.Code, problems);
        }

        var outcome = written.Value;
        if (outcome.Status == ExportStatus.Unchanged)
        {
            SimulateLog.PackUnchanged(logger, packId);
        }
        else
        {
            var bytes = outcome.Entries.Sum(e => e.Bytes);
            SimulateLog.PackWritten(logger, packId, plan.Rows.Count, bytes);
        }

        await console.Out.WriteAsync(string.Concat(packRuns.Select(r => RunBanner.Text(r.Labels))));
        await console.Out.WriteAsync(Summary(packId, outcome, plan, resolveTime, runTime, time.GetElapsedTime(phaseStart)));
        if (plan.Rows.Count == 0)
        {
            await console.Error.WriteLineAsync("warning: the pooled runs hold no trade, so the pack has no rows");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            await console.Error.WriteLineAsync("cancelled after the pack was written; it is complete in " + outcome.Folder);
            return SimulateCommand.Cancelled;
        }

        return SimulateCommand.Done;
    }

    private static List<ExportFile> Files(string seed, int count, string packId, string packKey, Guid? basketId, List<PackRun> runs, PackPlan plan, Dictionary<int, TradeCardData> cards)
    {
        var files = new List<ExportFile>
        {
            new(PackFiles.PackJsonName, sink => PackFiles.WritePackJson(sink, seed, count, packId, packKey, basketId, runs, plan)),
            new(PackFiles.ReadmeName, sink => sink.Write(Encoding.UTF8.GetBytes(PackFiles.ReadmeText(packId, seed, runs)))),
            new(CardNames.IndexName, sink => PackIndexHtml.Write(sink, packId, seed, runs, plan)),
            new(PackFiles.EngineValuesName, sink => PackFiles.WriteEngineValues(sink, plan)),
            new(PackFiles.CheckLogName, sink => PackFiles.WriteCheckLog(sink, plan)),
        };
        foreach (var row in plan.Rows)
        {
            var run = runs[row.RunIndex];
            var data = cards[row.Seq];
            files.Add(new ExportFile(PackFiles.CardName(row.Seq, plan.Rows.Count, row.Trade.Trade), sink => TradeCardHtml.Write(sink, data, run.Run, run.Labels)));
        }

        return files;
    }

    private static byte[] Render(Action<IBufferWriter<byte>> write)
    {
        var sink = new ArrayBufferWriter<byte>();
        write(sink);
        return sink.WrittenSpan.ToArray();
    }

    /// <summary>Before <c>--force</c> replaces a pack whose log the owner has started, a loud warning: the whole folder is kept as <c>.previous</c> and a blank log is written (never a second overwrite of an earlier copy, which the writer refuses).</summary>
    private static async Task WarnOnForceAsync(bool force, string packFolder, byte[] blankLog, SimulateConsole console)
    {
        if (!force || ExportPaths.IsLink(packFolder) || !Directory.Exists(packFolder))
        {
            return;
        }

        var log = Path.Combine(packFolder, PackFiles.CheckLogName);
        if (File.Exists(log) && !ExportPaths.IsLink(log) && !(await File.ReadAllBytesAsync(log)).AsSpan().SequenceEqual(blankLog))
        {
            await console.Error.WriteLineAsync(
                "WARNING: " + PackFiles.CheckLogName + " in " + packFolder + " has been edited. --force moves the whole folder, your log included, to " + packFolder + ExportPaths.PreviousSuffix
                + " and writes a blank log. If that folder already exists the replacement is refused.");
        }
    }

    /// <summary>The files of a pack folder that differ from its manifest, named, so that a refusal says which file was touched.</summary>
    private static List<string> ChangedFiles(string packFolder)
    {
        var lines = new List<string>();
        try
        {
            var path = Path.Combine(packFolder, ExportPaths.ManifestName);
            if (!File.Exists(path) || ExportPaths.IsLink(path))
            {
                return lines;
            }

            var manifest = ExportManifest.ParseBytes(File.ReadAllBytes(path));
            if (manifest.IsFailure)
            {
                return lines;
            }

            foreach (var entry in manifest.Value.Entries)
            {
                var file = Path.Combine(packFolder, entry.Name);
                var same = File.Exists(file) && !ExportPaths.IsLink(file) && new FileInfo(file).Length == entry.Bytes
                    && string.Equals(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))), entry.Sha256, StringComparison.Ordinal);
                if (!same)
                {
                    lines.Add("  differs from the manifest: " + entry.Name);
                }
            }
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            // the refusal stands without the names
        }

        return lines;
    }

    private static string Summary(string packId, ExportOutcome outcome, PackPlan plan, TimeSpan resolve, TimeSpan runs, TimeSpan cardsAndWrite)
    {
        var text = new StringBuilder();
        text.Append('\n');
        text.Append("pack: ").Append(packId).Append('\n');
        text.Append("folder: ").Append(outcome.Folder).Append('\n');
        text.Append("export: ").Append(outcome.Status switch
        {
            ExportStatus.Written => "written",
            ExportStatus.Unchanged => "unchanged (the identical pack was already there and every file was verified)",
            _ => "replaced (the old pack is kept in " + outcome.PreviousFolder + ")",
        }).Append('\n');
        text.Append("rows: ").Append(plan.Rows.Count.ToString(CultureInfo.InvariantCulture)).Append(" trades, ").Append(plan.Spots.Count.ToString(CultureInfo.InvariantCulture)).Append(" spot checks\n");
        if (outcome.StaleStaging.Count > 0)
        {
            text.Append("leftover staging folders of a killed process (remove them by hand): ").Append(string.Join(", ", outcome.StaleStaging)).Append('\n');
        }

        text.Append("time: resolve and gate ").Append(Ms(resolve)).Append(" ms, runs ").Append(Ms(runs)).Append(" ms, draw, cards and write ").Append(Ms(cardsAndWrite)).Append(" ms\n");
        text.Append("next: open index.html, fill check-log.csv, then run verify-pack --check <folder>/check-log.csv\n");
        return text.ToString();
    }

    private static string Ms(TimeSpan span) => ((long)span.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
}
