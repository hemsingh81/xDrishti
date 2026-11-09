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

    private static string Rel(string file) => Path.GetRelativePath(Src, file).Replace('\\', '/');

    /// <summary>The source of a file without whole-line comments, split into statements at ';' (a const SQL text, an EF query and a method call are each one statement).</summary>
    private static string[] Statements(string file) =>
        string.Join('\n', File.ReadAllLines(file).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))).Split(';');

    private const string Tables = "sim_(runs|trades|trade_legs|trade_leg_charges)";

    /// <summary>A table named with its schema (quoted or not, in any case), or a schema prefix glued to a variable table name.</summary>
    [GeneratedRegex("\"?research\"?\\s*\\.\\s*\"?" + Tables + "\"?\\b|research\\.\"\\s*\\+", RegexOptions.IgnoreCase)]
    private static partial Regex NamedTable();

    /// <summary>An EF set of a run table.</summary>
    [GeneratedRegex(@"\.Sim(Runs|Trades|TradeLegs|TradeLegCharges)\b|Set<\s*Sim(Run|Trade|TradeLeg|TradeLegCharge)Row\s*>")]
    private static partial Regex EfSet();

    private static readonly Regex WriteVerb = new(@"(INSERT\s+INTO|COPY|DELETE\s+FROM|UPDATE|TRUNCATE)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static readonly string[] RunFences = ["purpose = 'research'", "Purpose == RunPurposes.ResearchText"];

    /// <summary>
    /// Files allowed to name the run child tables (trades, legs, charges) or to write: they take their run ids from a fenced lookup (<c>FindRunAsync</c>, the fenced run-id statement of the report source) or write research runs. A new file is not on this list.
    /// </summary>
    private static readonly string[] ChildTableFiles =
    [
        "XDrishti.Infrastructure/Research/RunStore.Read.cs",
        "XDrishti.Infrastructure/Research/RunStore.Legs.cs",
        "XDrishti.Infrastructure/Research/RunStore.Write.cs",
        "XDrishti.Infrastructure/Research/ReportTradeSource.cs",
        "XDrishti.Infrastructure/Ai/Evaluation/FixtureSeeder.cs",
    ];

    private static readonly string[] NotEvidenceReads = ["XDrishti.Infrastructure/Ai/Evaluation/FixtureSeeder.cs", "XDrishti.Domain/Assistant/ManifestViews.cs"];

    private static IEnumerable<(string File, string Statement, string Table)> TableReads() =>
        Sources().SelectMany(f => Statements(f).SelectMany(st =>
            NamedTable().Matches(st).Where(m => !WriteVerb.IsMatch(st[..m.Index])).Select(m => (Rel(f), st, m.Value.ToLowerInvariant()))));

    [Fact]
    public void Every_statement_that_reads_sim_runs_carries_the_research_only_fence_and_no_other_file_names_a_child_table()
    {
        var reads = TableReads().ToList();
        reads.ShouldNotBeEmpty();
        foreach (var (file, statement, table) in reads)
        {
            if (NotEvidenceReads.Contains(file))
            {
                continue; // the fixture writes research runs; the manifest only names a source table
            }

            if (table.Contains("sim_runs", StringComparison.Ordinal))
            {
                if (file.EndsWith("RunStore.Write.cs", StringComparison.Ordinal) && statement.Contains("SelectStored =", StringComparison.Ordinal))
                {
                    continue; // SelectStored is the write path's idempotency check, which must see a run of either purpose
                }

                RunFences.Any(f => statement.Contains(f, StringComparison.Ordinal)).ShouldBeTrue($"{file}: a statement reads research.sim_runs without a purpose = 'research' fence (ADR 0243): {statement.Trim()[..Math.Min(120, statement.Trim().Length)]}");
            }
            else
            {
                ChildTableFiles.ShouldContain(file, $"{file} names {table}, which is read only through a fenced run lookup; add it here only with that lookup and a test in TrainingRunFenceTests (ADR 0243).");
            }
        }
    }

    [Fact]
    public void The_ef_sets_of_the_run_tables_are_used_on_exactly_the_known_lines()
    {
        var uses = Sources().SelectMany(f => File.ReadAllLines(f).Select((l, i) => (File: Rel(f), Line: l, No: i + 1)).Where(x => !x.Line.TrimStart().StartsWith("//", StringComparison.Ordinal) && EfSet().IsMatch(x.Line))).ToList();

        // the one door (EvidenceRuns) and the model registration; any other use of a set of these tables is a new reader without the fence
        uses.Where(u => u.File.EndsWith("RunStore.Read.cs", StringComparison.Ordinal)).Select(u => u.Line.Trim()).ShouldBe(["private IQueryable<SimRunRow> EvidenceRuns() => db.SimRuns.AsNoTracking().Where(x => x.Purpose == RunPurposes.ResearchText);"]);
        uses.Where(u => !u.File.EndsWith("RunStore.Read.cs", StringComparison.Ordinal)).Select(u => u.File).Distinct().Order(StringComparer.Ordinal).ShouldBe(["XDrishti.Infrastructure/Persistence/XDrishtiDbContext.cs"]);
    }

    [Fact]
    public void The_known_readers_of_sim_runs_are_exactly_the_fenced_ones()
    {
        // an addition to this list is a decision: the new reader is fenced and has a test in TrainingRunFenceTests
        var readers = TableReads().Where(r => r.Table.Contains("sim_runs", StringComparison.Ordinal) && !NotEvidenceReads.Contains(r.File) && !(r.File.EndsWith("RunStore.Write.cs", StringComparison.Ordinal) && r.Statement.Contains("SelectStored =", StringComparison.Ordinal))).Select(r => r.File).Distinct().Order(StringComparer.Ordinal).ToList();

        readers.ShouldBe(["XDrishti.Infrastructure/Research/ReportTradeSource.cs", "XDrishti.Infrastructure/Research/RunStore.Comparisons.cs"]);
    }

    [Fact]
    public void Every_ledger_read_is_filtered_by_kind_except_the_one_listing_and_its_readers_skip_training_sweeps()
    {
        var path = Path.Combine(Src, "XDrishti.Infrastructure", "Lab", "TestLedger.cs");
        var statements = Statements(path).Where(st => st.Contains("FROM lab.test_ledger", StringComparison.OrdinalIgnoreCase) && !st.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)).ToList();
        statements.ShouldNotBeEmpty();

        // a read passes only by leaving training sweeps out (<> 'training_sweep') or by naming an evidence kind; "= 'training_sweep'" is the separate count and is not a fence
        var unfenced = statements.Where(st => st.Split('\n').Where(l => l.Contains("FROM lab.test_ledger", StringComparison.OrdinalIgnoreCase)).Any(l => !(l.Contains("trial_kind <> 'training_sweep'", StringComparison.Ordinal) || Regex.IsMatch(l, "trial_kind = '(run|replan|null)'", RegexOptions.None, TimeSpan.FromSeconds(2)) || Regex.IsMatch(l, "trial_kind = 'training_sweep'", RegexOptions.None, TimeSpan.FromSeconds(2))))).ToList();
        unfenced.Count.ShouldBe(1, "exactly one ledger statement is unfiltered: the listing, which shows training sweeps beside the looks (ADR 0243)");
        unfenced[0].ShouldContain("ORDER BY created_at DESC");

        var users = Sources().Where(f => File.ReadAllText(f).Contains("ITestLedger", StringComparison.Ordinal) && File.ReadAllText(f).Contains("ledger.ListAsync(", StringComparison.Ordinal)).Select(Rel).Order(StringComparer.Ordinal).ToList();
        users.ShouldBe(["XDrishti.Application/Learning/ComparisonEvaluation.cs", "XDrishti.Application/Lab/GetLedgerHandler.cs", "XDrishti.Application/Lab/LabDeflation.cs"], ignoreOrder: true);
        foreach (var user in users.Where(u => !u.EndsWith("GetLedgerHandler.cs", StringComparison.Ordinal)))
        {
            File.ReadAllText(Path.Combine(Src, user)).ShouldContain("IsTrainingSweep", Case.Sensitive, $"{user} takes hashes from the ledger listing and must skip training sweeps");
        }

        var others = Sources().Where(f => !f.EndsWith("TestLedger.cs", StringComparison.Ordinal) && Regex.IsMatch(File.ReadAllText(f), @"FROM\s+""?lab""?\s*\.\s*""?test_ledger", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2))).Select(Rel).ToList();
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

        var views = new (string View, string Fence)[] { ("aiv.sim_runs", "purpose = 'research'"), ("aiv.sim_trades", "purpose = 'research'"), ("aiv.sim_trade_legs", "purpose = 'research'"), ("aiv.test_ledger", "trial_kind AS trial_kind") };
        foreach (var file in migrations.Skip(fenceAt))
        {
            var text = File.ReadAllText(file);
            foreach (var (view, fence) in views)
            {
                if (Regex.IsMatch(text, @"CREATE\s+(OR\s+REPLACE\s+)?VIEW\s+" + Regex.Escape(view) + @"\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)) && !file.Contains("AddSimRunPurpose", StringComparison.Ordinal))
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
        // the purpose is chosen only where a RunToStore or RunHeader is made for training; today nothing does (AU-12 will), so no source outside the model names Training
        var users = Sources().Where(f => File.ReadAllText(f).Contains("RunPurpose.Training", StringComparison.Ordinal)).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        users.ShouldBe(["RunAssembler.cs", "RunPurposes.cs", "RunStore.Write.cs"]);
    }
}
