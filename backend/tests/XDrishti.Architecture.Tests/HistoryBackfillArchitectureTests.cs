namespace XDrishti.Architecture.Tests;

/// <summary>CF-04 Part B (ADR 0270): the ledger and the seam table are written and read by one store only, and the bar store is begun for the Dhan source by the backfill and the existing pipeline only.</summary>
public sealed class HistoryBackfillArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static IEnumerable<string> SourceFiles() =>
        Directory.GetFiles(Src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    [Fact]
    public void The_backfill_tables_are_named_in_code_only_by_the_backfill_store()
    {
        string[] names = ["backfill_chunks", "backfill_seams"];
        var offenders = SourceFiles()
            .Where(f => Path.GetFileName(f) != "BackfillStore.cs")
            .Where(f => File.ReadAllLines(f).Any(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal) && names.Any(n => l.Contains(n, StringComparison.Ordinal))))
            .Select(f => Path.GetRelativePath(Src, f))
            .ToList();

        offenders.ShouldBeEmpty("only BackfillStore.cs (and the migrations, excluded here) may name the backfill tables in SQL");
    }

    // The readers that need every stored bar: the pipeline that writes them, the repair, the quality checks and the backfill itself. Every other caller is a simulation, learning, planning or report path and must read through SeamAwareBars.
    private static readonly string[] RawReaders =
    [
        "XDrishti.Application.Market.Bars.Pipeline.DhanPhaseRunner",
        "XDrishti.Application.Market.Bars.Repair.RepairBarHolesHandler",
        "XDrishti.Application.Market.History.ProbeSeamHandler",
        "XDrishti.Application.Market.History.RunBackfillHandler",
        "XDrishti.Application.Market.History.SeamAwareBars",
        "XDrishti.Application.Market.Quality.GetQualityInstrumentHandler",
        "XDrishti.Application.Market.Quality.QualityPhaseRunner",
    ];

    [Fact]
    public void Only_the_pipeline_the_repair_the_quality_checks_and_the_backfill_read_bars_raw_every_other_path_goes_through_the_seam_aware_read()
    {
        var assemblies = new[] { typeof(XDrishti.Application.DependencyInjection).Assembly, typeof(XDrishti.Infrastructure.Persistence.DatabaseOptions).Assembly, System.Reflection.Assembly.Load(new System.Reflection.AssemblyName("XDrishti.Api")) };
        var callers = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(assembly.Location);
            foreach (var type in module.GetTypes())
            {
                var root = type;
                while (root.DeclaringType is not null)
                {
                    root = root.DeclaringType;
                }

                if (type.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "ReadAsync" && call.DeclaringType.FullName == "XDrishti.Application.Abstractions.Market.Bars.IBarStore"))
                {
                    callers.Add(root.FullName);
                }
            }
        }

        callers.ToList().ShouldBe(RawReaders, ignoreOrder: true, customMessage: "a simulation, learning, planning or report path reads bars through SeamAwareBars (ADR 0270, decision 10)");
    }

    [Fact]
    public void The_one_read_of_a_run_drops_the_bars_before_the_seam()
    {
        var reader = File.ReadAllText(Path.Combine(Src, "XDrishti.Application", "Research", "InstrumentInputReader.cs"));

        reader.ShouldContain("SeamAwareBars.ReadForRunAsync");
    }

    // CF-04d decision 6: the run reader decides admission afresh (ReadForRunAsync, only from InstrumentInputReader.ReadAsync); the readers that follow a stored run (the cards through the same reader, the feature extraction that training uses, the trade replay) go through ReadRecordedAsync.
    private static SortedSet<string> CallersOf(string method)
    {
        var callers = new SortedSet<string>(StringComparer.Ordinal);
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(typeof(XDrishti.Application.DependencyInjection).Assembly.Location);
        foreach (var type in module.GetTypes())
        {
            var root = type;
            while (root.DeclaringType is not null)
            {
                root = root.DeclaringType;
            }

            if (root.FullName == "XDrishti.Application.Market.History.SeamAwareBars")
            {
                continue;
            }

            foreach (var m in type.Methods.Where(x => x.HasBody))
            {
                if (m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == method && call.DeclaringType.FullName == "XDrishti.Application.Market.History.SeamAwareBars"))
                {
                    // an async method's body is its state machine's MoveNext: name the enclosing type and the outer method it was made from
                    callers.Add(root.FullName + "." + (type.DeclaringType is null ? m.Name : type.Name.Split('>')[0].TrimStart('<')));
                }
            }
        }

        return callers;
    }

    [Fact]
    public void The_fresh_admission_read_is_made_only_by_the_run_reader_and_the_stored_run_reads_only_by_the_stored_run_readers()
    {
        CallersOf("ReadForRunAsync").ToList().ShouldBe(["XDrishti.Application.Research.InstrumentInputReader.ReadAsync"], "only the run reader decides admission afresh (CF-04d, ADR 0273)");
        CallersOf("ReadRecordedAsync").ToList().ShouldBe(
        [
            "XDrishti.Application.Learning.ExtractTradeFeaturesHandler.ExtractAsync",
            "XDrishti.Application.Reporting.GetTradeReplayHandler.HandleAsync",
            "XDrishti.Application.Research.InstrumentInputReader.ReadRecordedAsync",
        ], ignoreOrder: true, customMessage: "a reader that follows a stored run reads through ReadRecordedAsync and never decides afresh");
    }

    [Fact]
    public void The_admission_facts_are_read_only_from_the_seam_aware_read_and_the_history_report()
    {
        var offenders = SourceFiles()
            .Where(f => Path.GetFileName(f) is not ("PreSeamAdmission.cs" or "IBackfillStore.cs" or "BackfillStore.cs"))
            .Where(f => File.ReadAllText(f).Contains("AdmissionFactsAsync", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Src, f))
            .ToList();

        offenders.ShouldBeEmpty("PreSeamAdmission is the one place that turns the stored facts into a decision");
    }

    [Fact]
    public void The_backfill_writer_passes_every_bar_through_the_seam_guard_before_it_merges()
    {
        var runner = File.ReadAllText(Path.Combine(Src, "XDrishti.Application", "Market", "History", "RunBackfillHandler.cs"));

        runner.IndexOf("SeamGuard.Split", StringComparison.Ordinal).ShouldBeGreaterThan(0);
        runner.IndexOf("SeamGuard.Split", StringComparison.Ordinal).ShouldBeLessThan(runner.IndexOf("WriteAsync(target.InstrumentId", StringComparison.Ordinal));
        runner.ShouldNotContain("BarSource.Csv");
    }

    // Files that read market.bars_1m or market.bars_agg for the pipeline itself (writing, aggregating, comparing, counting, quality, the observed calendar), for display, or for a recent window that never reaches a seam (liquidity universe, marks,
    // live execution, the daily-versus-minute finding); each is listed in ADR 0270 ("not covered"). Every other file that reads those tables must apply market.backfill_cut.
    private static readonly string[] NotCut =
    [
        "BarStore.cs", "BarAggregates.cs", "BarComparer.cs", "BarDayCounts.cs", "BarCoverageSql.cs", "BarRecords.cs", "BarConfigurations.cs", "QualityData.cs", "ObservedDays.cs",
        "EodStore.cs", "DepthUniverse.cs", "OptionUnderlyingSetting.cs", "PaperLedgerData.cs", "PortfolioPrices.cs", "LiveExecutionMarket.cs", "FixtureSeeder.cs", "BackfillStore.cs",
    ];

    [Fact]
    public void Every_raw_sql_reader_of_the_bar_tables_outside_the_listed_ones_applies_the_backfill_cut()
    {
        var infrastructure = Path.Combine(Src, "XDrishti.Infrastructure");
        var offenders = Directory.GetFiles(infrastructure, "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(f), @"market\.bars_1m\b(?!_)|market\.bars_agg\b", System.Text.RegularExpressions.RegexOptions.None, ScanRegex.Timeout))
            .Where(f => !NotCut.Contains(Path.GetFileName(f)) && !File.ReadAllText(f).Contains("backfill_cut", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Src, f))
            .ToList();

        offenders.ShouldBeEmpty("a raw SQL reader of bars_1m or bars_agg that feeds research, context or planning must apply market.backfill_cut(instrument_id) (ADR 0270, decision 10), or be listed with its reason");
    }
}
