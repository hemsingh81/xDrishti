using System.Text.RegularExpressions;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// AU-11 (ADR 0243): a <c>purpose = training</c> run, and a <c>training_sweep</c> ledger row, must be invisible to every evidence reader (Lab verdicts, the readiness scorecard, cell lifecycle, plan selection, ML promotion, the ledger counts, the AI
/// views). The readers all go through the run store, the report source, the ledger store or the aiv views, so the pin sits at the data access: any source file that reads one of those tables must carry the fence, and a new view or migration that redefines
/// one of the views without it fails here. A new reader that forgets the fence cannot pass this test; the database-backed <c>TrainingRunFenceTests</c> proves the readers that exist.
/// </summary>
public sealed partial class EvidenceReaderFenceArchitectureTests
{
    private static string Src => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj"))!)!;

    private static IEnumerable<string> Sources() =>
        Directory.EnumerateFiles(Src, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>The ways source reads the run tables: SQL text or the EF set. Comments and the write path are not reads.</summary>
    [GeneratedRegex(@"(FROM|JOIN)\s+research\.sim_(runs|trades)\b|\.SimRuns\b|Set<SimRunRow>")]
    private static partial Regex RunTableRead();

    private static readonly string[] RunFences = ["purpose = 'research'", "RunPurposes.ResearchText"];

    /// <summary>Files that touch the run tables without being an evidence reader: the model's own registration, the single-run write path, and the evaluation fixture seeder (writes research runs).</summary>
    private static readonly string[] NotReaders = ["XDrishtiDbContext.cs", "RunStore.Write.cs", "FixtureSeeder.cs"];

    [Fact]
    public void Every_source_that_reads_the_run_tables_carries_the_research_only_fence()
    {
        var readers = Sources().Where(f => !NotReaders.Contains(Path.GetFileName(f)) && RunTableRead().IsMatch(File.ReadAllText(f))).ToList();

        readers.ShouldNotBeEmpty();
        foreach (var file in readers)
        {
            RunFences.Any(f => File.ReadAllText(file).Contains(f, StringComparison.Ordinal)).ShouldBeTrue($"{Path.GetFileName(file)} reads research.sim_runs or sim_trades without a purpose = 'research' fence (ADR 0243). Read runs through RunStore.EvidenceRuns() or add the fence.");
        }
    }

    [Fact]
    public void The_known_readers_of_the_run_tables_are_exactly_the_fenced_ones()
    {
        // an addition to this list is a decision: the new reader is fenced and has a test in TrainingRunFenceTests
        var readers = Sources().Where(f => !NotReaders.Contains(Path.GetFileName(f)) && RunTableRead().IsMatch(File.ReadAllText(f))).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        readers.ShouldBe(["ReportTradeSource.cs", "RunStore.Comparisons.cs", "RunStore.Read.cs"]);
    }

    [Fact]
    public void Every_statement_that_reads_the_ledger_leaves_out_training_sweeps_or_names_its_kind()
    {
        var lines = File.ReadAllLines(Path.Combine(Src, "XDrishti.Infrastructure", "Lab", "TestLedger.cs"))
            .Where(l => l.Contains("FROM lab.test_ledger", StringComparison.Ordinal) && !l.Contains("INSERT", StringComparison.Ordinal))
            .ToList();

        lines.ShouldNotBeEmpty();
        foreach (var line in lines)
        {
            (line.Contains("training_sweep", StringComparison.Ordinal) || line.Contains("trial_kind = '", StringComparison.Ordinal)).ShouldBeTrue("A ledger read without the training_sweep fence: " + line.Trim());
        }

        var others = Sources().Where(f => Path.GetFileName(f) != "TestLedger.cs" && File.ReadAllText(f).Contains("FROM lab.test_ledger", StringComparison.Ordinal)).Select(Path.GetFileName).ToList();
        others.ShouldBeEmpty("Only TestLedger reads lab.test_ledger; a second reader needs the fence and a test.");
    }

    [Fact]
    public void A_migration_that_redefines_an_evidence_view_keeps_the_fence()
    {
        var migrations = Directory.EnumerateFiles(Path.Combine(Src, "XDrishti.Infrastructure", "Persistence", "Migrations"), "2*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
        var fenceAt = migrations.FindIndex(f => Path.GetFileName(f).Contains("AddSimRunPurpose", StringComparison.Ordinal));
        fenceAt.ShouldBeGreaterThan(0);

        var views = new (string View, string Fence)[] { ("aiv.sim_runs", "purpose = 'research'"), ("aiv.sim_trades", "purpose = 'research'"), ("aiv.sim_trade_legs", "purpose = 'research'"), ("aiv.test_ledger", "trial_kind <> 'training_sweep'") };
        foreach (var file in migrations.Skip(fenceAt))
        {
            var text = File.ReadAllText(file);
            foreach (var (view, fence) in views)
            {
                if (Regex.IsMatch(text, @"CREATE\s+(OR\s+REPLACE\s+)?VIEW\s+" + Regex.Escape(view) + @"\b"))
                {
                    text.ShouldContain(fence, Case.Sensitive, $"{Path.GetFileName(file)} defines {view} without the training fence (ADR 0243)");
                }
            }
        }
    }

    [Fact]
    public void The_training_window_has_no_order_path_and_no_reference_to_the_execution_service()
    {
        var text = File.ReadAllText(Path.Combine(Src, "XDrishti.Application", "Lab", "TrainingWindowResolver.cs"));

        text.ShouldNotContain("XDrishti.Execution");
        text.ShouldNotContain("IBrokerOrder");
        text.ShouldNotContain("PlaceOrder");
    }

    [Fact]
    public void The_training_purpose_is_set_by_no_endpoint_and_no_existing_writer()
    {
        // the purpose is chosen only where a RunToStore is made for training; today nothing does (AU-12 will), so no source outside the model names Training
        var users = Sources().Where(f => File.ReadAllText(f).Contains("RunPurpose.Training", StringComparison.Ordinal)).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        users.ShouldBe(["RunPurposes.cs"]);
    }
}
