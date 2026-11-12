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
    private static string StripComments(string text) =>
        Regex.Replace(Regex.Replace(text, @"/\*.*?\*/", " ", RegexOptions.Singleline, ScanRegex.Timeout), @"(?<=^|\s)//[^\n]*", " ", RegexOptions.Multiline, ScanRegex.Timeout);

    /// <summary>The code of a file without any comment (whole-line, trailing or block), split into statements at ';' (a const SQL text, an EF query and a method call are each one statement).</summary>
    private static string[] Statements(string file) => StripComments(File.ReadAllText(file)).Split(';');

    private const string Tables = "sim_(runs|trades|trade_legs|trade_leg_charges)";

    /// <summary>A table named with its schema (quoted or not, in any case), or a schema prefix glued to a variable table name.</summary>
    [GeneratedRegex("\"?research\"?\\s*\\.\\s*\"?" + Tables + "\"?\\b|research\\.\"\\s*\\+", RegexOptions.IgnoreCase)]
    private static partial Regex NamedTable();

    /// <summary>An EF set of a run table.</summary>
    [GeneratedRegex(@"\.Sim(Runs|Trades|TradeLegs|TradeLegCharges)\b|Set<\s*Sim(Run|Trade|TradeLeg|TradeLegCharge)Row\s*>")]
    private static partial Regex EfSet();

    private static readonly Regex WriteVerb = new(@"(INSERT\s+INTO|COPY|DELETE\s+FROM|UPDATE|TRUNCATE)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, ScanRegex.Timeout);

    /// <summary>The fence as a conjunct: <c>WHERE/AND purpose = 'research'</c> in SQL, or <c>Purpose == RunPurposes.ResearchText</c> in a Where; an <c>OR</c> beside it is not a fence.</summary>
    private static bool HasFence(string statement) =>
        Regex.IsMatch(statement, @"\b(WHERE|AND)\s+(\w+\.)?purpose\s*=\s*'research'|\.Where\(\s*\w+\s*=>\s*\w+\.Purpose\s*==\s*RunPurposes\.ResearchText\s*\)", RegexOptions.IgnoreCase, ScanRegex.Timeout)
        && !Regex.IsMatch(statement, @"\bOR\s+(\w+\.)?purpose\b|\|\|\s*\w+\.Purpose", RegexOptions.IgnoreCase, ScanRegex.Timeout);

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

    /// <summary>AU-12 (ADR 0248): the one reader of training runs. It is the mirror of the fence: each of its statements carries <c>purpose = 'training'</c>.</summary>
    private const string TrainingReader = "XDrishti.Infrastructure/SimAll/TrainingRunReader.cs";

    private static bool HasTrainingFence(string statement) =>
        Regex.IsMatch(statement, @"\bWHERE\s+(\w+\.)?purpose\s*=\s*'training'\s+AND\b", RegexOptions.IgnoreCase, ScanRegex.Timeout)
        && !Regex.IsMatch(statement, @"\bOR\s+(\w+\.)?purpose\b", RegexOptions.IgnoreCase, ScanRegex.Timeout);

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

            if (file == TrainingReader)
            {
                (table.Contains("sim_runs", StringComparison.Ordinal) || table.Contains("sim_trades", StringComparison.Ordinal)).ShouldBeTrue($"{file} reads only the run header table and the closed trades of a run it found under the training fence (AU-13)");
                HasTrainingFence(statement).ShouldBeTrue($"{file}: a statement reads research.sim_runs without a purpose = 'training' fence (ADR 0248)");
                continue;
            }

            if (table.Contains("sim_runs", StringComparison.Ordinal))
            {
                if (file.EndsWith("RunStore.Write.cs", StringComparison.Ordinal) && statement.Contains("SelectStored =", StringComparison.Ordinal))
                {
                    continue; // SelectStored is the write path's idempotency check, which must see a run of either purpose
                }

                HasFence(statement).ShouldBeTrue($"{file}: a statement reads research.sim_runs without a purpose = 'research' fence (ADR 0243): {statement.Trim()[..Math.Min(120, statement.Trim().Length)]}");
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
        var uses = Sources().SelectMany(f => StripComments(File.ReadAllText(f)).Split('\n').Select((l, i) => (File: Rel(f), Line: l, No: i + 1)).Where(x => EfSet().IsMatch(x.Line))).ToList();

        // the one door (EvidenceRuns) and the model registration; any other use of a set of these tables is a new reader without the fence
        uses.Where(u => u.File.EndsWith("RunStore.Read.cs", StringComparison.Ordinal)).Select(u => u.Line.Trim()).ShouldBe(["private IQueryable<SimRunRow> EvidenceRuns() => db.SimRuns.AsNoTracking().Where(x => x.Purpose == RunPurposes.ResearchText);"]);
        uses.Where(u => !u.File.EndsWith("RunStore.Read.cs", StringComparison.Ordinal)).Select(u => u.File).Distinct().Order(StringComparer.Ordinal).ShouldBe(["XDrishti.Infrastructure/Persistence/XDrishtiDbContext.cs"]);
    }

    [Fact]
    public void The_known_readers_of_sim_runs_are_exactly_the_fenced_ones()
    {
        // an addition to this list is a decision: the new reader is fenced and has a test in TrainingRunFenceTests
        var readers = TableReads().Where(r => r.Table.Contains("sim_runs", StringComparison.Ordinal) && !NotEvidenceReads.Contains(r.File) && !(r.File.EndsWith("RunStore.Write.cs", StringComparison.Ordinal) && r.Statement.Contains("SelectStored =", StringComparison.Ordinal))).Select(r => r.File).Distinct().Order(StringComparer.Ordinal).ToList();

        readers.ShouldBe(["XDrishti.Infrastructure/Research/ReportTradeSource.cs", "XDrishti.Infrastructure/Research/RunStore.Comparisons.cs", TrainingReader]);
    }

    [Fact]
    public void Every_ledger_read_is_filtered_by_kind_except_the_one_listing_and_its_readers_skip_training_sweeps()
    {
        var path = Path.Combine(Src, "XDrishti.Infrastructure", "Lab", "TestLedger.cs");
        var statements = Statements(path).Where(st => st.Contains("FROM lab.test_ledger", StringComparison.OrdinalIgnoreCase) && !st.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)).ToList();
        statements.ShouldNotBeEmpty();

        // a read passes only by leaving training sweeps out (<> 'training_sweep') or by naming an evidence kind; "= 'training_sweep'" is the separate count and is not a fence
        var unfenced = statements.Where(st => st.Split('\n').Where(l => l.Contains("FROM lab.test_ledger", StringComparison.OrdinalIgnoreCase)).Any(l => !(l.Contains("trial_kind <> 'training_sweep'", StringComparison.Ordinal) || Regex.IsMatch(l, "trial_kind = '(run|replan|null|system_replay)'", RegexOptions.None, ScanRegex.Timeout) || Regex.IsMatch(l, "trial_kind = 'training_sweep'", RegexOptions.None, ScanRegex.Timeout)))).ToList();
        unfenced.Count.ShouldBe(1, "exactly one ledger statement is unfiltered: the listing, which shows training sweeps beside the looks (ADR 0243)");
        unfenced[0].ShouldContain("ORDER BY created_at DESC");

        var users = Sources().Where(f => File.ReadAllText(f).Contains("ITestLedger", StringComparison.Ordinal) && File.ReadAllText(f).Contains("ledger.ListAsync(", StringComparison.Ordinal)).Select(Rel).Order(StringComparer.Ordinal).ToList();
        users.ShouldBe(["XDrishti.Application/Learning/ComparisonEvaluation.cs", "XDrishti.Application/Lab/GetLedgerHandler.cs", "XDrishti.Application/Lab/LabDeflation.cs"], ignoreOrder: true);
        foreach (var user in users.Where(u => !u.EndsWith("GetLedgerHandler.cs", StringComparison.Ordinal)))
        {
            File.ReadAllText(Path.Combine(Src, user)).ShouldContain("IsTrainingSweep", Case.Sensitive, $"{user} takes hashes from the ledger listing and must skip training sweeps");
        }

        var others = Sources().Where(f => !f.EndsWith("TestLedger.cs", StringComparison.Ordinal) && Regex.IsMatch(File.ReadAllText(f), @"FROM\s+""?lab""?\s*\.\s*""?test_ledger", RegexOptions.IgnoreCase, ScanRegex.Timeout)).Select(Rel).ToList();
        others.ShouldBeEmpty("Only TestLedger reads lab.test_ledger; a second reader needs the fence and a test.");
    }

    [Fact]
    public void No_source_names_a_run_table_without_its_schema_or_with_an_interpolated_schema()
    {
        var bare = new Regex(@"(?<![\w.])sim_(runs|trades|trade_legs|trade_leg_charges)\b", RegexOptions.IgnoreCase, ScanRegex.Timeout);
        var interpolated = new Regex(@"\{[^}]+\}\s*\.\s*""?sim_(runs|trades|trade_legs|trade_leg_charges)|Schemas\.Research\s*\+", RegexOptions.IgnoreCase, ScanRegex.Timeout);
        foreach (var file in Sources().Where(f => !NotEvidenceReads.Contains(Rel(f)) && !Rel(f).EndsWith("RunStore.Legs.cs", StringComparison.Ordinal) /* the leg tables are named by a variable there and found by the concatenation rule */ && !Rel(f).Contains("/Persistence/Configurations/", StringComparison.Ordinal)))
        {
            var code = StripComments(File.ReadAllText(file));
            bare.IsMatch(code).ShouldBeFalse($"{Rel(file)} names a run table without a schema; the pin cannot see it (ADR 0243)");
            interpolated.IsMatch(code).ShouldBeFalse($"{Rel(file)} builds a run table name from an interpolated schema; the pin cannot see it (ADR 0243)");
        }
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

        var viewStatement = new Regex(@"CREATE\s+(OR\s+REPLACE\s+)?(MATERIALIZED\s+)?VIEW\s+(?<name>[\w.]+)", RegexOptions.IgnoreCase, ScanRegex.Timeout);
        var runTables = new Regex(@"sim_(runs|trades|trade_legs|trade_leg_charges)\b", RegexOptions.IgnoreCase, ScanRegex.Timeout);
        var checkedViews = 0;
        foreach (var file in migrations.Skip(fenceAt))
        {
            var text = File.ReadAllText(file);
            if (file.Contains("AddSimRunPurpose", StringComparison.Ordinal))
            {
                text = text[..text.IndexOf("protected override void Down", StringComparison.Ordinal)]; // Up only: Down restores the old views on purpose
            }

            // each CREATE VIEW statement is checked on its own: a fence elsewhere in the file does not count
            foreach (var chunk in text.Split("migrationBuilder.Sql(").Skip(1))
            {
                var statement = StripComments(chunk.Split("\"\"\");")[0]);
                if (viewStatement.Match(statement) is not { Success: true } view)
                {
                    continue;
                }

                if (runTables.IsMatch(statement))
                {
                    HasFence(statement).ShouldBeTrue($"{Path.GetFileName(file)} defines {view.Groups["name"].Value} over the run tables without the training fence (ADR 0243)");
                    checkedViews++;
                }
                else if (view.Groups["name"].Value.Equals("aiv.test_ledger", StringComparison.OrdinalIgnoreCase))
                {
                    statement.ShouldContain("trial_kind AS trial_kind", Case.Sensitive, $"{Path.GetFileName(file)} defines aiv.test_ledger without the kind column");
                    checkedViews++;
                }
            }
        }

        checkedViews.ShouldBe(4);
    }

    [Theory]
    [InlineData("SELECT 1 FROM research.sim_runs r WHERE r.purpose = 'research'", true)]
    [InlineData("SELECT 1 FROM research.sim_runs r WHERE x = 1 AND purpose = 'research' ORDER BY id", true)]
    [InlineData("SELECT 1 FROM research.sim_runs r WHERE x = 1 OR purpose = 'research'", false)]
    [InlineData("SELECT 1 FROM research.sim_runs r -- purpose = 'research'", false)]
    [InlineData("db.SimRuns.Where(x => x.Purpose == RunPurposes.ResearchText)", true)]
    [InlineData("db.SimRuns.Where(x => x.Purpose == RunPurposes.ResearchText || x.Id > 0)", false)]
    public void The_fence_check_accepts_only_a_conjunct_predicate_outside_comments(string statement, bool fenced) =>
        HasFence(StripComments(statement)).ShouldBe(fenced);

    [Fact]
    public void The_training_window_has_no_order_path_and_no_reference_to_the_execution_service()
    {
        var text = File.ReadAllText(Path.Combine(Src, "XDrishti.Application", "Lab", "TrainingWindowResolver.cs"));

        text.ShouldNotContain("XDrishti.Execution");
        text.ShouldNotContain("IBrokerOrder");
        text.ShouldNotContain("PlaceOrder");
    }

    [Fact]
    public void The_training_reader_is_named_only_by_the_learning_side_and_never_by_evidence_code()
    {
        // AU-12 (ADR 0243 point 7, ADR 0248): the reader of training runs is not evidence. Only its port, its implementation, the registration and the learning side may name it; Lab, Confidence, Selection, Reporting,
        // Planning, Research (the run store) and the readiness scorecard may not. AU-13 added the one handler of the learning side (SimTrainingArchitectureTests pins what it may name).
        // AU-19 (ADR 0253): the programme check reads only the run key and end date of training runs, to prove each finished job stopped before its hold-out; it is a read-only fence check, not evidence, and decides nothing.
        var allowed = new[] { "XDrishti.Application/Abstractions/SimAll/ITrainingRunReader.cs", "XDrishti.Infrastructure/SimAll/TrainingRunReader.cs", "XDrishti.Infrastructure/SimAll/TrainingReportSource.cs", "XDrishti.Infrastructure/DependencyInjection.cs", "XDrishti.Application/Learning/SimTraining/TrainFromSimulationHandler.cs", "XDrishti.Application/Auto/EvaluateAutoProgrammeCheckHandler.cs" };
        var users = Sources().Where(f => File.ReadAllText(f).Contains("TrainingRunReader", StringComparison.Ordinal)).Select(Rel).Order(StringComparer.Ordinal).ToList();

        users.ShouldBe([.. allowed.Order(StringComparer.Ordinal)]);
        Sources().Where(f => Rel(f).StartsWith("XDrishti.Application/SimAll/", StringComparison.Ordinal)).Select(f => File.ReadAllText(f)).ShouldAllBe(t => !t.Contains("ITrainingRunReader", StringComparison.Ordinal), "the job writes training runs and never reads them back");
    }

    [Fact]
    public void The_report_engine_reaches_training_runs_only_through_the_training_report_port_and_the_evidence_source_never_reads_them()
    {
        // AU-14 (ADR 0250): the report engine is evidence code. It may name the PORT of training reports (one place, the loader), never the reader, and no statement of it may name the training purpose; the evidence report source stays research-only.
        var port = Sources().Where(f => File.ReadAllText(f).Contains("ITrainingReportSource", StringComparison.Ordinal)).Select(Rel).Order(StringComparer.Ordinal).ToList();

        port.ShouldBe(["XDrishti.Application/Abstractions/Reporting/ITrainingReportSource.cs", "XDrishti.Application/Reporting/ReportLoader.cs", "XDrishti.Infrastructure/Ai/ReadOnly/AiReadOnlyRegistration.cs", "XDrishti.Infrastructure/Ai/ReadOnly/NoTrainingReports.cs", "XDrishti.Infrastructure/DependencyInjection.cs", "XDrishti.Infrastructure/SimAll/TrainingReportSource.cs"]);
        foreach (var file in Sources().Where(f => Rel(f).StartsWith("XDrishti.Application/Reporting/", StringComparison.Ordinal) || Rel(f).EndsWith("Research/ReportTradeSource.cs", StringComparison.Ordinal)))
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("ITrainingRunReader", Case.Sensitive, Rel(file));
            text.ShouldNotContain("purpose = 'training'", Case.Sensitive, Rel(file));
        }
    }

    [Fact]
    public void The_simulate_everything_job_has_no_order_path_and_no_reference_to_the_execution_service()
    {
        var files = Sources().Where(f => Rel(f).StartsWith("XDrishti.Application/SimAll/", StringComparison.Ordinal) || Rel(f).StartsWith("XDrishti.Infrastructure/SimAll/", StringComparison.Ordinal) || Rel(f).EndsWith("Endpoints/SimAllEndpoints.cs", StringComparison.Ordinal)).ToList();
        files.Count.ShouldBeGreaterThan(5);
        foreach (var file in files)
        {
            var code = StripComments(File.ReadAllText(file));
            code.ShouldNotContain("XDrishti.Execution", Case.Sensitive, Rel(file));
            code.ShouldNotContain("IBrokerOrder", Case.Sensitive, Rel(file));
            code.ShouldNotContain("PlaceOrder", Case.Sensitive, Rel(file));
            code.ShouldNotContain("IBroker", Case.Sensitive, Rel(file) + ": the job reads stored bars and calls no broker");
        }
    }

    [Fact]
    public void The_training_purpose_is_set_by_no_endpoint_and_no_existing_writer()
    {
        // the purpose is chosen only where a RunToStore or RunHeader is made for training; only the simulate-everything chunk executor does (AU-12), so no source outside the model and it names Training
        var users = Sources().Where(f => File.ReadAllText(f).Contains("RunPurpose.Training", StringComparison.Ordinal)).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        users.ShouldBe(["RunAssembler.cs", "RunPurposes.cs", "RunStore.Write.cs", "RunToStore.cs", "SimAllChunkExecutor.cs"]);
    }

    [Fact]
    public void No_evidence_reader_names_the_replay_tables_or_the_replay_store()
    {
        // SR-01 (AC 11, ADR 0272): a replay is research, not evidence. Cell statistics sources, Lab verdicts, the readiness scorecard, the decay monitor and model training read none of its tables. Only the store, its ports, the job and the registrations name them; a reader of the
        // replay (the API of part C, the comparison of part D) is added to this list on purpose, and never one of the evidence readers.
        var name = new Regex(@"\bIReplayStore\b|\bIReplayReads\b|\bReplayStore\b|\bReplayRunRow\b|\breplay_(runs|sessions|trades)\b", RegexOptions.CultureInvariant, ScanRegex.Timeout);
        var users = Sources().Where(f => name.IsMatch(StripComments(File.ReadAllText(f)))).Select(Rel).Order(StringComparer.Ordinal).ToList();

        users.ShouldBe(
        [
            "XDrishti.Application/Abstractions/Replay/IReplayStore.cs",
            "XDrishti.Application/Abstractions/Replay/ReplayModels.cs",
            "XDrishti.Application/Replay/Job/CancelReplayHandler.cs",
            "XDrishti.Application/Replay/Job/ReplayRunner.cs",
            "XDrishti.Application/Replay/Job/StartReplayHandler.cs",
            "XDrishti.Application/Replay/Reads/ReplayReadSupport.cs", // part C: the replay's own read side, behind the owner-only /api/replay routes; never an evidence reader
            "XDrishti.Application/Replay/Reads/RunReads.cs",
            "XDrishti.Application/Replay/Reads/SeriesReads.cs",
            "XDrishti.Application/Replay/Reads/StartReplayRequestHandler.cs",
            "XDrishti.Application/Replay/Reads/TradePairReads.cs",
            "XDrishti.Application/SimAll/SimAllRunner.cs",
            "XDrishti.Infrastructure/DependencyInjection.cs",
            "XDrishti.Infrastructure/Replay/ReplayStore.cs",
        ]);
        name.IsMatch("FROM research.replay_sessions").ShouldBeTrue("the scan does see a use");

        foreach (var evidence in new[] { "Lab", "Confidence", "Learning", "Reporting", "Selection", "Planning", "Research", "Shadow", "Paper", "Autonomy", "Auto", "LearningPhase" })
        {
            foreach (var layer in new[] { "XDrishti.Application", "XDrishti.Infrastructure" })
            {
                var dir = Path.Combine(Src, layer, evidence);
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories).Where(f => !RepositoryFiles.IsBuildOutput(f)))
                {
                    StripComments(File.ReadAllText(file)).ShouldNotContain("XDrishti.Application.Abstractions.Replay", Case.Sensitive, Rel(file) + " is an evidence reader and must not read the replay (SR-01, AC 11)");
                }
            }
        }
    }
}
