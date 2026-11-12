using System.Text.RegularExpressions;
using NetArchTest.Rules;
using XDrishti.Application.Replay;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// SR-01 part A (ADR 0272): the system replay is research only. It names no execution project and no order, Planning does not name it, it reads bars only through the clamped reader, and it calls none of the stores or gates the live nightly writes through.
/// </summary>
public sealed partial class ReplayArchitectureTests
{
    private static readonly System.Reflection.Assembly Application = typeof(SystemReplayOptions).Assembly;
    private static readonly System.Reflection.Assembly Domain = typeof(XDrishti.Domain.Replay.BlockReason).Assembly;
    private static readonly System.Reflection.Assembly Infrastructure = typeof(XDrishti.Infrastructure.DependencyInjection).Assembly;

    [GeneratedRegex(@"\.\s*\w*(Store|Append|Insert|Save|Write|Publish|Delete|Update|Move)\w*Async\(", RegexOptions.CultureInvariant)]
    private static partial Regex WriteCall();

    [GeneratedRegex(@"PlaceOrder|IOrder\w*|\bBroker\w*|\bExecution\w*|IExec\w*", RegexOptions.CultureInvariant)]
    private static partial Regex Banned();

    private static IEnumerable<string> Files()
    {
        var src = Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj")))!;
        return new[] { Path.Combine("XDrishti.Domain", "Replay"), Path.Combine("XDrishti.Application", "Replay"), Path.Combine("XDrishti.Infrastructure", "Replay") }
            .SelectMany(d => Directory.GetFiles(Path.Combine(src, d), "*.cs"));
    }

    [Fact]
    public void The_replay_names_no_execution_project_broker_or_order()
    {
        Files().Count().ShouldBeGreaterThan(20, "the scan sees the replay files");
        foreach (var file in Files())
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("XDrishti.Execution", customMessage: file);
            Banned().IsMatch(text).ShouldBeFalse(file);
        }

        foreach (var (assembly, ns) in new[] { (Domain, "XDrishti.Domain.Replay"), (Application, "XDrishti.Application.Replay"), (Infrastructure, "XDrishti.Infrastructure.Replay") })
        {
            var result = Types.InAssembly(assembly).That().ResideInNamespace(ns).ShouldNot().HaveDependencyOn("XDrishti.Execution").GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void Planning_does_not_name_a_replay_type()
    {
        var domain = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Planning").ShouldNot().HaveDependencyOn("XDrishti.Domain.Replay").GetResult();
        var application = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Planning").ShouldNot().HaveDependencyOn("XDrishti.Application.Replay").GetResult();
        var infrastructure = Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.Planning").ShouldNot().HaveDependencyOn("XDrishti.Application.Replay").GetResult();

        domain.IsSuccessful.ShouldBeTrue(string.Join(", ", domain.FailingTypeNames ?? []));
        application.IsSuccessful.ShouldBeTrue(string.Join(", ", application.FailingTypeNames ?? []));
        infrastructure.IsSuccessful.ShouldBeTrue(string.Join(", ", infrastructure.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_replay_reaches_stored_bars_only_through_the_clamped_reader()
    {
        // only ClampedBars calls the bar ports; ReplayFacts holds them just to build it; nothing else of the replay (the session runner, the driver, the candidate source) names a bar port
        var others = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Replay").And().DoNotHaveName("ClampedBars").And().DoNotHaveName("ReplayFacts")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Market.Bars.IBarStore", "XDrishti.Application.Abstractions.Market.Bars.IBarQueries").GetResult();
        var infrastructure = Types.InAssembly(Infrastructure).That().ResideInNamespace("XDrishti.Infrastructure.Replay")
            .ShouldNot().HaveDependencyOnAny("XDrishti.Application.Abstractions.Market.Bars.IBarStore", "XDrishti.Application.Abstractions.Market.Bars.IBarQueries").GetResult();
        others.IsSuccessful.ShouldBeTrue(string.Join(", ", others.FailingTypeNames ?? []));
        infrastructure.IsSuccessful.ShouldBeTrue(string.Join(", ", infrastructure.FailingTypeNames ?? []));

        var facts = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Replay", "ReplayFacts.cs"));
        facts.ShouldNotContain("minuteBars.", customMessage: "ReplayFacts must read minute bars through ClampedBars only");
        facts.ShouldNotContain("dayBars.", customMessage: "ReplayFacts must read day bars through ClampedBars only");
        File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Replay", "ClampedBars.cs")).ShouldContain("ReplayHoldOutException");
    }

    [Fact]
    public void The_replay_calls_no_nightly_gate_no_plan_store_and_no_write()
    {
        var result = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Replay")
            .ShouldNot().HaveDependencyOnAny(
                "XDrishti.Application.Planning.RunNightlyPlanHandler", "XDrishti.Application.Planning.INightlyGate", "XDrishti.Application.Abstractions.Planning.IPlanStore",
                "XDrishti.Application.Abstractions.Paper.IPaperStore", "XDrishti.Application.Abstractions.Planning.IGradeStore", "XDrishti.Application.Abstractions.Planning.IJournalStore")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));

        foreach (var file in Files())
        {
            WriteCall().IsMatch(File.ReadAllText(file)).ShouldBeFalse(file);
        }
    }

    private static string Src() => Path.GetDirectoryName(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj")))!;

    private static readonly string[] WriteReceivers = ["store", "ledger"];

    private static string[] JobFiles() => Directory.GetFiles(Path.Combine(Src(), "XDrishti.Application", "Replay", "Job"), "*.cs");

    [GeneratedRegex(@"(?<receiver>\w+|\))\s*\.\s*\w*(Store|Append|Insert|Save|Write|Publish|Delete|Update|Move|Finish|Cancel|Claim|Recover|Mark)\w*Async\(", RegexOptions.CultureInvariant)]
    private static partial Regex ReceiverOfWrite();

    [GeneratedRegex(@"(INSERT\s+INTO|(?<!FOR\s+)UPDATE|DELETE\s+FROM|TRUNCATE(\s+TABLE)?|COPY|MERGE\s+INTO)\s+(?<table>[\w.]+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SqlWrite();

    [Fact]
    public void The_job_names_no_execution_project_broker_or_order_and_writes_only_through_the_replay_store_and_the_trial_ledger()
    {
        var files = JobFiles().ToList();
        files.Count.ShouldBeGreaterThan(4, "the scan sees the job files");
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            text.ShouldNotContain("XDrishti.Execution", customMessage: file);
            Banned().IsMatch(text).ShouldBeFalse(file);
            foreach (Match write in ReceiverOfWrite().Matches(text))
            {
                WriteReceivers.ShouldContain(write.Groups["receiver"].Value, $"{Path.GetFileName(file)}: {write.Value} writes through something other than the replay store or the ledger port (SR-01, AC 11)");
            }
        }
    }

    private static readonly string[] ResolvedAllowed = ["IReplayStore", "IReplayReads", "ReplayDriver", "ReplayInputHasher", "IFreeDiskProbe", "ITradingCalendar", "ISimAllBackfillGate", "ISimAllStore"];

    private static readonly string[] ConstructorAllowed = ["IServiceScopeFactory", "SystemReplayOptions", "TimeProvider", "ILogger`1", "ReplayDriver", "ReplayInputHasher", "IReplayInputs", "IReplayStore", "IReplayReads", "ITestLedger", "ISimAllBackfillGate"];

    [GeneratedRegex(@"Get(Required)?Service<(?<t>\w+)>", RegexOptions.CultureInvariant)]
    private static partial Regex ResolvedType();

    [Fact]
    public void The_job_resolves_and_takes_in_its_constructors_only_the_listed_types_so_no_other_store_or_gate_can_be_reached()
    {
        // SR-01 (AC 11): a write through anything the receiver scan does not name (a chained GetRequiredService<T>().XAsync(...), a verb it does not know) still needs a type; a type outside these two lists fails here and is a decision.
        foreach (var file in JobFiles())
        {
            foreach (Match m in ResolvedType().Matches(File.ReadAllText(file)))
            {
                ResolvedAllowed.ShouldContain(m.Groups["t"].Value, $"{Path.GetFileName(file)} resolves {m.Value}");
            }
        }

        var types = Application.GetTypes().Where(t => t.Namespace == "XDrishti.Application.Replay.Job" && (t.Name.EndsWith("Runner", StringComparison.Ordinal) || t.Name.EndsWith("Handler", StringComparison.Ordinal))).ToList();
        types.Count.ShouldBe(3, "the runner and the two handlers");
        foreach (var type in types)
        {
            foreach (var parameter in type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SelectMany(c => c.GetParameters()))
            {
                ConstructorAllowed.ShouldContain(parameter.ParameterType.Name, $"{type.Name} takes {parameter.ParameterType.Name}");
            }
        }

        ConstructorAllowed.ShouldNotContain("IPlanStore", "the sentinel: a plan store is never allowed");
        ResolvedType().Match("sp.GetRequiredService<IPlanStore>().AppendAsync(x)").Groups["t"].Value.ShouldBe("IPlanStore");
        ReceiverOfWrite().Match("await sp.GetRequiredService<IPlanStore>().FinishAsync(x)").Groups["receiver"].Value.ShouldBe(")", "a chained write has no named receiver and fails the allow-list of receivers");
    }

    [Fact]
    public void The_replay_may_write_only_its_three_tables_and_only_the_one_store_names_them()
    {
        // AC 11 (ADR 0272): the whole list of what a replay writes. Planning, paper, shadow, learning, model and settings tables are not on it; the trial-ledger row goes through the ledger port, which is the ledger's own writer.
        string[] allowed = ["research.replay_runs", "research.replay_sessions", "research.replay_sweeps", "research.replay_trades"]; // SR-02 added the insert-only sweep table
        var store = File.ReadAllText(Path.Combine(Src(), "XDrishti.Infrastructure", "Replay", "ReplayStore.cs"));
        var written = SqlWrite().Matches(store).Select(m => m.Groups["table"].Value.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal).ToList();

        written.ShouldBe(allowed);
        Regex.IsMatch(store, @"DELETE\s+FROM|TRUNCATE", RegexOptions.IgnoreCase).ShouldBeFalse("the replay tables are never deleted from in this slice");
        foreach (var file in Directory.GetFiles(Path.Combine(Src(), "XDrishti.Infrastructure", "Replay"), "*.cs", SearchOption.AllDirectories).Where(f => !f.EndsWith("ReplayStore.cs", StringComparison.Ordinal)))
        {
            SqlWrite().IsMatch(File.ReadAllText(file)).ShouldBeFalse(file + " writes SQL: only ReplayStore does");
        }

        var namers = Directory.EnumerateFiles(Src(), "*.cs", SearchOption.AllDirectories)
            .Where(f => !RepositoryFiles.IsBuildOutput(f) && !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && Regex.IsMatch(string.Join("\n", File.ReadAllLines(f).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))), @"replay_(runs|sessions|sweeps|trades)\b"))
            .Select(f => Path.GetRelativePath(Src(), f).Replace('\\', '/')).ToList();
        namers.ShouldBe(["XDrishti.Infrastructure/Replay/ReplayStore.cs"]);
        SqlWrite().IsMatch("INSERT INTO research.replay_runs (a)").ShouldBeTrue("the scan does see a write");
        SqlWrite().IsMatch("UPDATE research.replay_runs SET").ShouldBeTrue();
        SqlWrite().IsMatch("SELECT 1 FROM research.replay_runs").ShouldBeFalse("a read is not a write");
        SqlWrite().IsMatch("SELECT id FROM research.replay_runs FOR UPDATE SKIP LOCKED").ShouldBeFalse("a row lock is not a write");
    }

    [Fact]
    public void The_scans_see_what_they_are_meant_to_see_sentinels()
    {
        // the write-call pattern matches the write verbs a store has, and the bar-port scan sees the one type that may hold them
        WriteCall().IsMatch("await store.AppendAsync(x, ct);").ShouldBeTrue();
        WriteCall().IsMatch("await store.InsertOnceAsync(x, ct);").ShouldBeTrue();
        WriteCall().IsMatch("await plans.UpdateStateAsync(x, ct);").ShouldBeTrue();
        WriteCall().IsMatch("await facts.CellsAsync(p, d, ct);").ShouldBeFalse();
        ReceiverOfWrite().Match("await store.SaveSessionAsync(x, ct);").Groups["receiver"].Value.ShouldBe("store");
        ReceiverOfWrite().Match("await planStore.AppendAsync(x, ct);").Groups["receiver"].Value.ShouldBe("planStore");
        Types.InAssembly(Application).That().HaveName("ClampedBars").Should().HaveDependencyOn("XDrishti.Application.Abstractions.Market.Bars.IBarStore").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().HaveName("ReplayFacts").Should().HaveDependencyOn("XDrishti.Application.Abstractions.Market.Bars.IBarStore").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_closed_list_of_block_reasons_is_the_eight_of_the_spec()
    {
        XDrishti.Domain.Replay.BlockReasons.All.Select(XDrishti.Domain.Replay.BlockReasons.Code).ShouldBe(["gate", "smart_rule", "max_positions", "total_risk", "cluster_cap", "capital", "data_missing", "no_version"]);
    }
}
