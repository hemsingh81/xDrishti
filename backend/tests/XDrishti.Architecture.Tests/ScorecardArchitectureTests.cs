using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Domain.Readiness;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// CF-11 part 1 (ADR 0221): the readiness scorecard is derived and read only; it can refuse an owner's lifecycle move when the owner's enforce switch is ON and it can do nothing else. Enforced here, each scan with a positive control and a sentinel it must catch: (1) the calculators
/// are pure Domain; (2) nothing that chooses what to approve (Planning, Selection, Autonomy) names any type of the slice; (3) the lifecycle recorder is the only caller of the gate and the only route to the scorecard, and the slice never builds or sends a lifecycle command or appends
/// to the changelog, so no path moves a cell without the owner; (4) the stores have the few writers they must have and the switch is stored state and not a configuration value; (5) the one source of the inputs that have no producer says unavailable and only the scorecard reader reads it;
/// (6) the routes are three reads and the owner's switch, a fresh password only for ON; (7) nothing touches an order or the execution plane. (The P12-11 broker readiness probe has its own rules in <c>ReadinessArchitectureTests</c>; this is another thing.)
/// </summary>
public sealed class ScorecardArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;
    private static readonly Assembly Self = typeof(ScorecardArchitectureTests).Assembly;

    private static readonly string[] ProductionNames =
    [
        "XDrishti.Api", "XDrishti.Application", "XDrishti.Domain", "XDrishti.Execution", "XDrishti.Feed", "XDrishti.Hosting", "XDrishti.Indicators", "XDrishti.Infrastructure", "XDrishti.Learning.MlNet", "XDrishti.Mcp",
        "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Worker",
    ];

    // every Domain type of the slice, by simple name (the namespace XDrishti.Domain.Readiness holds exactly these)
    private static readonly string[] DomainNames =
    [
        nameof(ReadinessOptions), nameof(RowStatus), nameof(ChecklistRow), nameof(ChecklistInputs), nameof(EdgeChecklist), nameof(GateResult), nameof(ReadinessGates), nameof(ReadinessLabel), nameof(ReadinessLabels),
        nameof(ScoreAnchors), nameof(SubScoreState), nameof(SubScore), nameof(BandPosition), nameof(ScoreInputs), nameof(FaultState), nameof(HardFailInputs), nameof(ReadinessScore), nameof(ReadinessCalculator),
        nameof(StrategyReadiness), nameof(SystemReadiness), nameof(SystemReadinessCalculator), nameof(ReadinessAssessment),
    ];

    private static readonly string[] DomainTypes = [.. DomainNames.Select(n => "XDrishti.Domain.Readiness." + n)];

    // what a consumer must not name: the calculators and options, the handlers and readers, the views, the ports and the gate
    private static readonly string[] SliceTypes =
    [
        .. DomainTypes,
        .. new[]
        {
            "ScorecardReader", "LabEvidenceReader", "ForwardEvidenceReader", "ForwardEdgeReader", "ForwardEdge", "ForwardEvidence", "LabEvidence", "GetScorecardHandler", "GetScorecardQuery", "ListScorecardsHandler", "ListScorecardsQuery",
            "GetReadinessSettingsHandler", "GetReadinessSettingsQuery", "ChangeReadinessSwitchHandler", "ChangeReadinessSwitchCommand", "SnapshotScorecardsHandler", "SnapshotScorecardsCommand", "ReadinessSnapshotService", "ReadinessGate",
            "UnavailableReadinessGaps", "ScorecardView", "ScorecardListView", "ReadinessSettingsView", "ReadinessErrors", "ReadinessOptionsView",
        }.Select(n => "XDrishti.Application.Confidence." + n),
        .. new[]
        {
            "IReadinessGate", "ReadinessGateKinds", "ReadinessGateKind", "ReadinessGateRequest", "ReadinessGateDecision", "IReadinessGaps", "IReadinessSettings", "IReadinessSwitchStore", "IReadinessSnapshotStore", "IReadinessSnapshotReader",
            "ReadinessSwitch", "NewReadinessSnapshot",
        }.Select(n => "XDrishti.Application.Abstractions.Confidence." + n),
    ];

    private static readonly (Assembly Assembly, string Namespace)[] Choosers =
    [
        (Domain, "XDrishti.Domain.Selection"), (Domain, "XDrishti.Domain.Planning"), (Domain, "XDrishti.Domain.Autonomy"),
        (Application, "XDrishti.Application.Planning"), (Application, "XDrishti.Application.Selection"), (Application, "XDrishti.Application.Autonomy"),
    ];

    /// <summary>Stands for a decision type that reads the scorecard and a gate.</summary>
    internal sealed class ChooserSentinel(IReadinessGate gate)
    {
        public ValueTask<ReadinessGateDecision> Ask() => gate.CheckAsync(new ReadinessGateRequest("a|b|c|d|e|f", ReadinessGateKind.ResearchToPaper, "x"), CancellationToken.None);

        public static double Score() => ReadinessCalculator.Compute(new ScoreInputs(), HardFailInputs.AllClear, new ReadinessOptions()).Score;
    }

    /// <summary>Stands for a second writer of the owner's switch and a second reader of the gaps.</summary>
    internal sealed class SecondWriterSentinel(IReadinessSwitchStore store, IReadinessGaps gaps)
    {
        public ValueTask<long> Write() => store.AppendAsync(true, "owner:x", "y", CancellationToken.None);

        public ValueTask<ReadinessReading<bool>> Read() => gaps.IntegrityFaultAsync(CancellationToken.None);
    }

    /// <summary>Stands for a calculator that reads the clock.</summary>
    internal sealed class ClockSentinel(TimeProvider clock)
    {
        public DateTimeOffset Now() => clock.GetUtcNow();
    }

    /// <summary>Stands for a type of the slice that moves a cell itself.</summary>
    internal sealed class LifecycleSentinel
    {
        public static XDrishti.Application.Learning.RecordCellTransitionCommand Command(string cell) => new(cell, XDrishti.Domain.Learning.CellState.Paper, "why", "auto", new XDrishti.Domain.Learning.TransitionFacts(), ["e"]);
    }

    // ---- the IL of every production type (and the sentinels), read once ----

    private static readonly Lazy<Dictionary<string, TypeDefinition>> Index = new(() =>
    {
        var types = new Dictionary<string, TypeDefinition>(StringComparer.Ordinal);
        foreach (var path in ProductionNames.Select(n => Assembly.Load(new AssemblyName(n)).Location).Append(Self.Location).Distinct())
        {
            foreach (var type in ModuleDefinition.ReadModule(path).GetTypes())
            {
                types[type.FullName] = type;
            }
        }

        return types;
    });

    /// <summary>The top-level type a compiler-generated nested type (a closure, a state machine) belongs to.</summary>
    private static string Root(string fullName)
    {
        var parts = fullName.Replace('+', '/').Split('/');
        return string.Join('/', parts.TakeWhile((p, i) => i == 0 || !p.StartsWith('<')));
    }

    /// <summary>The top-level types whose code calls or reads a member the predicate accepts (a state machine counts for its type).</summary>
    private static List<string> Callers(Func<MemberReference, bool> member) =>
        [.. Index.Value.Values.Where(t => t.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MemberReference r && member(r))).Select(t => Root(t.FullName)).Distinct().Order(StringComparer.Ordinal)];

    private static bool CallsOn(MemberReference r, string method, Type declaring) => r.Name == method && r.DeclaringType?.FullName == declaring.FullName;

    private static bool IsCtorOf(MemberReference r, string type) => r.Name == ".ctor" && r.DeclaringType?.FullName == type;

    private static List<string> Dependents(Assembly assembly, string contract) =>
        [.. Types.InAssembly(assembly).That().HaveDependencyOn(contract).GetTypes().Select(t => Root(t.FullName!)).Distinct().Order(StringComparer.Ordinal)];

    private static string Sentinel(Type type) => type.FullName!.Replace('+', '/');

    // ---- (1) the calculators are pure ----

    [Fact]
    public void The_calculators_are_pure_no_clock_no_io_no_system_random_and_name_no_application_type()
    {
        var pure = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Readiness");

        pure.GetTypes().Where(t => !t.Name.StartsWith('<')).Select(t => t.Name).Order(StringComparer.Ordinal).ShouldBe(DomainNames.Order(StringComparer.Ordinal), "control: the list is the set of types the rule ran over");
        var result = pure.Should().NotHaveDependencyOnAny(
            "System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics.Process", "System.Diagnostics.Stopwatch", "System.Diagnostics.Activity", "System.TimeProvider", "System.Environment", "System.Random",
            "System.Security.Cryptography", "System.Text.Json", "Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Application", "XDrishti.Infrastructure").GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        foreach (var name in new[] { "EdgeChecklist", "ReadinessCalculator", "SystemReadinessCalculator", "ScoreAnchors", "ReadinessGates", "ReadinessAssessment" })
        {
            var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Readiness", name + ".cs"));
            source.ShouldNotContain("DateTime", customMessage: name);
            source.ShouldNotContain("new Random", customMessage: name);
            source.ShouldNotContain("Random.Shared", customMessage: name);
        }

        // control: the same rule sees a dependency where there is one (a type that uses the clock)
        Types.InAssembly(Self).That().HaveName(nameof(ClockSentinel)).Should().NotHaveDependencyOnAny("System.TimeProvider").GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_lab_figures_behind_rows_10_and_11_are_pure_and_the_verdict_evaluator_does_not_read_them()
    {
        // SC-S1 (ADR 0222): the figures are computed at Lab run time by pure Domain types (the bootstrap draws only from the seeded generator) and the verdict never reads them
        string[] names = [nameof(XDrishti.Domain.Lab.QuarterBreakdown), nameof(XDrishti.Domain.Lab.QuarterRow), nameof(XDrishti.Domain.Lab.InstrumentShare), nameof(XDrishti.Domain.Lab.MonteCarlo), nameof(XDrishti.Domain.Lab.MonteCarloResult)];
        var pure = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Lab").And().HaveNameMatching("^(" + string.Join("|", names) + ")$");

        pure.GetTypes().Select(t => t.Name).Order(StringComparer.Ordinal).ShouldBe(names.Order(StringComparer.Ordinal), "control: the rule ran over exactly these types");
        var result = pure.Should().NotHaveDependencyOnAny(
            "System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics.Process", "System.Diagnostics.Stopwatch", "System.TimeProvider", "System.Environment", "System.Random",
            "System.Security.Cryptography", "System.Text.Json", "Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Application", "XDrishti.Infrastructure", "XDrishti.Domain.Readiness").GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        foreach (var name in new[] { "QuarterBreakdown", "InstrumentShare" })
        {
            var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Lab", name + ".cs"));
            source.ShouldNotContain("DateTime", customMessage: name);
            source.ShouldNotContain("Random", customMessage: name);
        }

        var verdict = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Lab", "VerdictEvaluator.cs"));
        foreach (var word in new[] { "QuarterBreakdown", "InstrumentShare", "DrawdownP90", "MonteCarlo", "Robustness" })
        {
            verdict.ShouldNotContain(word);
        }

        Types.InAssembly(Self).That().HaveName(nameof(ClockSentinel)).Should().NotHaveDependencyOnAny("System.TimeProvider").GetResult().IsSuccessful.ShouldBeFalse(); // control
    }

    // ---- (2) nothing that chooses reads the slice ----

    [Fact]
    public void Planning_selection_and_autonomy_name_nothing_of_the_slice()
    {
        foreach (var (assembly, ns) in Choosers)
        {
            var types = Types.InAssembly(assembly).That().ResideInNamespace(ns);
            types.GetTypes().ShouldNotBeEmpty(ns);
            var result = types.Should().NotHaveDependencyOnAny(SliceTypes).GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        Types.InAssembly(Self).That().HaveName(nameof(ChooserSentinel)).Should().NotHaveDependencyOnAny(SliceTypes).GetResult().IsSuccessful.ShouldBeFalse("control: the rule sees a type that asks the gate and a type that scores");
        SliceTypes.ShouldAllBe(n => Application.GetType(n) != null || Domain.GetType(n) != null, "control: every name of the list is a real type, so the rule cannot pass by naming nothing");
    }

    [Fact]
    public void The_slice_is_named_only_inside_itself_the_lifecycle_recorder_and_the_registration()
    {
        // Application: the slice's own namespaces are left out; what else names it is the lifecycle recorder (the gate and its error) and the registration
        var users = Types.InAssembly(Application).That().HaveDependencyOnAny(SliceTypes.Where(n => n.StartsWith("XDrishti.Application", StringComparison.Ordinal)).ToArray()).GetTypes()
            .Where(t => t.Namespace is not ("XDrishti.Application.Confidence" or "XDrishti.Application.Abstractions.Confidence") && !t.Name.StartsWith('<'))
            .Select(t => Root(t.FullName!).Split('.')[^1]).Distinct().Order(StringComparer.Ordinal).ToList();

        users.ShouldBe(["DependencyInjection", "RecordCellTransitionHandler"]);

        // Domain: nothing outside the slice names a calculator
        Types.InAssembly(Domain).That().DoNotResideInNamespace("XDrishti.Domain.Readiness").Should().NotHaveDependencyOnAny(DomainTypes).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Self).That().HaveName(nameof(ChooserSentinel)).Should().HaveDependencyOnAny(DomainTypes).GetResult().IsSuccessful.ShouldBeTrue(); // control
    }

    // ---- (3) the lifecycle recorder is the only caller of the gate; nothing moves a cell on its own ----

    [Fact]
    public void Only_the_lifecycle_recorder_asks_the_gate_and_builds_its_request()
    {
        Callers(r => CallsOn(r, "CheckAsync", typeof(IReadinessGate))).ShouldBe(["XDrishti.Application.Learning.RecordCellTransitionHandler", Sentinel(typeof(ChooserSentinel))]);
        Callers(r => IsCtorOf(r, typeof(ReadinessGateRequest).FullName!)).Where(n => n != typeof(ReadinessGateRequest).FullName).ShouldBe(["XDrishti.Application.Learning.RecordCellTransitionHandler", Sentinel(typeof(ChooserSentinel))]);
        Dependents(Application, typeof(IReadinessGate).FullName!).ShouldBe(["XDrishti.Application.Confidence.ReadinessGate", "XDrishti.Application.DependencyInjection", "XDrishti.Application.Learning.RecordCellTransitionHandler"]);

        // the recorder reaches the scorecard through the gate and its error only
        var recorder = Types.InAssembly(Application).That().HaveName("RecordCellTransitionHandler");
        recorder.Should().NotHaveDependencyOnAny("XDrishti.Application.Confidence.ScorecardReader", "XDrishti.Application.Confidence.ScorecardView", "XDrishti.Application.Confidence.LabEvidenceReader", "XDrishti.Application.Confidence.ForwardEvidenceReader", "XDrishti.Domain.Readiness").GetResult().IsSuccessful.ShouldBeTrue();
        recorder.Should().HaveDependencyOn(typeof(IReadinessGate).FullName!).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_scorecard_has_the_few_readers_it_must_have_and_the_gate_judges_only_the_two_moves()
    {
        Dependents(Application, "XDrishti.Application.Confidence.ScorecardReader").ShouldBe(
        [
            "XDrishti.Application.Confidence.GetScorecardHandler", "XDrishti.Application.Confidence.ListScorecardsHandler", "XDrishti.Application.Confidence.ReadinessGate", "XDrishti.Application.Confidence.ScorecardReader", "XDrishti.Application.Confidence.SnapshotScorecardsHandler",
            "XDrishti.Application.DependencyInjection",
        ]);

        ReadinessGateKinds.For(XDrishti.Domain.Learning.CellState.Candidate, XDrishti.Domain.Learning.CellState.Paper).ShouldBe(ReadinessGateKind.ResearchToPaper);
        ReadinessGateKinds.For(XDrishti.Domain.Learning.CellState.Paper, XDrishti.Domain.Learning.CellState.Active).ShouldBe(ReadinessGateKind.PaperToActive);
        ReadinessGateKinds.For(XDrishti.Domain.Learning.CellState.Paused, XDrishti.Domain.Learning.CellState.Paper).ShouldBe(ReadinessGateKind.ResearchToPaper, "the owner's re-enable is judged by gate R (CF-12 D3)");
        foreach (var from in Enum.GetValues<XDrishti.Domain.Learning.CellState>())
        {
            foreach (var to in Enum.GetValues<XDrishti.Domain.Learning.CellState>())
            {
                var judged = ReadinessGateKinds.For(from, to) is not null;
                judged.ShouldBe((from, to) is (XDrishti.Domain.Learning.CellState.Candidate, XDrishti.Domain.Learning.CellState.Paper) or (XDrishti.Domain.Learning.CellState.Paused, XDrishti.Domain.Learning.CellState.Paper) or (XDrishti.Domain.Learning.CellState.Paper, XDrishti.Domain.Learning.CellState.Active), $"{from} to {to}");
            }
        }
    }

    [Fact]
    public void The_slice_never_builds_sends_or_records_a_lifecycle_move_and_names_no_order_or_execution_type()
    {
        string[] lifecycle =
        [
            "XDrishti.Application.Learning.RecordCellTransitionCommand", "XDrishti.Application.Learning.PauseCellCommand", "XDrishti.Application.Confidence.ReEnableCellCommand", "XDrishti.Application.Confidence.ApplyKillCommand",
            "XDrishti.Application.Learning.RevertChangelogCommand", "XDrishti.Application.Abstractions.Learning.NewChangelogEntry", "XDrishti.Application.Abstractions.Messaging.ICommandHandler`2<XDrishti.Application.Learning.RecordCellTransitionCommand",
        ];
        string[] forbidden =
        [
            "XDrishti.Execution", "XDrishti.Application.Execution", "XDrishti.Application.Abstractions.Execution", "XDrishti.Application.Abstractions.LiveTrading", "XDrishti.Application.LiveTrading",
            "XDrishti.Application.Abstractions.Accounts", "XDrishti.Application.Accounts", "XDrishti.Application.Autonomy", "XDrishti.Application.Abstractions.Autonomy", "XDrishti.Application.Planning", "XDrishti.Application.Selection",
        ];
        var ours = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Confidence").And().HaveNameMatching("^(Scorecard|LabEvidence|Forward|GetScorecard|ListScorecards|GetReadiness|ChangeReadiness|SnapshotScorecards|ReadinessGate|ReadinessSnapshotService|ReadinessOptionsView|UnavailableReadinessGaps|ReadinessErrors|ReadinessLog).*$");

        ours.GetTypes().Where(t => !t.Name.StartsWith('<')).Count().ShouldBeGreaterThan(15, "control: the rule runs over the slice");
        var result = ours.Should().NotHaveDependencyOnAny([.. lifecycle, .. forbidden]).GetResult();
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));

        // the IL says the same: nobody in the slice constructs the lifecycle command or calls the changelog append
        static bool InSlice(string name) => name.StartsWith("XDrishti.Application.Confidence.", StringComparison.Ordinal) && !name.Contains("Kill", StringComparison.Ordinal) && !name.Contains("ReEnable", StringComparison.Ordinal) && !name.Contains("Decay", StringComparison.Ordinal) && !name.Contains("StrategyCells", StringComparison.Ordinal);
        var builders = Callers(r => IsCtorOf(r, "XDrishti.Application.Learning.RecordCellTransitionCommand"));
        var appenders = Callers(r => r.Name == "AppendAsync" && r.DeclaringType?.FullName == "XDrishti.Application.Abstractions.Learning.ILearningChangelog");
        builders.ShouldContain("XDrishti.Application.Confidence.ReEnableCellHandler", "control: the scan sees the real builders");
        builders.ShouldContain(Sentinel(typeof(LifecycleSentinel)), "control: and the sentinel");
        appenders.ShouldContain("XDrishti.Application.Learning.RecordCellTransitionHandler", "control: the scan sees the real appender");
        builders.Where(InSlice).ShouldBeEmpty();
        appenders.Where(InSlice).ShouldBeEmpty();
        Types.InAssembly(Self).That().HaveName(nameof(LifecycleSentinel)).Should().NotHaveDependencyOnAny(lifecycle).GetResult().IsSuccessful.ShouldBeFalse("control: the rule sees a type that builds the command");
    }

    // ---- (4) who writes what ----

    [Fact]
    public void The_switch_has_one_writer_the_owners_handler_and_the_snapshots_have_two_writers_and_one_reader()
    {
        Dependents(Application, typeof(IReadinessSwitchStore).FullName!).ShouldBe(["XDrishti.Application.Confidence.ChangeReadinessSwitchHandler"]);
        Callers(r => CallsOn(r, "AppendAsync", typeof(IReadinessSwitchStore))).ShouldBe(["XDrishti.Application.Confidence.ChangeReadinessSwitchHandler", Sentinel(typeof(SecondWriterSentinel))], "the one caller, and the control");
        Dependents(Application, typeof(IReadinessSettings).FullName!).ShouldBe(
        [
            "XDrishti.Application.Confidence.GetReadinessSettingsHandler", "XDrishti.Application.Confidence.GetScorecardHandler", "XDrishti.Application.Confidence.ListScorecardsHandler", "XDrishti.Application.Confidence.ReadinessGate",
        ]);
        Dependents(Application, typeof(IReadinessSnapshotStore).FullName!).ShouldBe(["XDrishti.Application.Confidence.ReadinessGate", "XDrishti.Application.Confidence.SnapshotScorecardsHandler"]);
        Dependents(Application, typeof(IReadinessSnapshotReader).FullName!).ShouldBe(["XDrishti.Application.Confidence.GetScorecardHandler"]);

        // implementations: one each, in Infrastructure
        foreach (var contract in new[] { typeof(IReadinessSwitchStore), typeof(IReadinessSettings), typeof(IReadinessSnapshotStore), typeof(IReadinessSnapshotReader) })
        {
            var implementations = ProductionNames.SelectMany(n => Assembly.Load(new AssemblyName(n)).GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && contract.IsAssignableFrom(t)).Select(t => t.Namespace).Distinct().ToList();
            implementations.ShouldBe(["XDrishti.Infrastructure.Confidence"], contract.Name);
        }

        Types.InAssembly(Self).That().HaveName(nameof(SecondWriterSentinel)).Should().HaveDependencyOn(typeof(IReadinessSwitchStore).FullName!).GetResult().IsSuccessful.ShouldBeTrue("control");
    }

    [Fact]
    public void The_switch_is_stored_state_and_not_a_configuration_value()
    {
        var settings = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Confidence", "ConfidenceSettings.cs"));
        var section = settings[settings.IndexOf("internal sealed class ReadinessSection", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("internal sealed class RegistrationSection", StringComparison.Ordinal)];

        section.ShouldContain("MinNEff");
        section.ShouldNotContain("Enforce");
        section.ShouldNotContain("Auto");
        section.ShouldNotContain("Enabled");
        typeof(ReadinessOptions).GetProperties().Select(p => p.Name).ShouldNotContain(n => n.Contains("Enforce", StringComparison.Ordinal) || n.Contains("Auto", StringComparison.Ordinal) || n.Contains("Enabled", StringComparison.Ordinal));
    }

    // ---- (5) the one source of the inputs that have no producer ----

    [Fact]
    public void The_one_gap_source_says_unavailable_and_only_the_scorecard_reader_reads_the_port()
    {
        static List<string> Implementations(params Assembly[] assemblies) =>
            [.. assemblies.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IReadinessGaps).IsAssignableFrom(t)).Select(t => t.FullName!).Order(StringComparer.Ordinal)];

        var assemblies = ProductionNames.Select(n => Assembly.Load(new AssemblyName(n))).ToArray();

        Implementations(assemblies).ShouldBe(["XDrishti.Application.Confidence.UnavailableReadinessGaps"]);
        Implementations([.. assemblies, Self]).ShouldContain(typeof(SecondReadinessGapSentinel).FullName!); // control: a second source is caught by the same scan
        Dependents(Application, typeof(IReadinessGaps).FullName!).ShouldBe(["XDrishti.Application.Confidence.ScorecardReader", "XDrishti.Application.Confidence.UnavailableReadinessGaps", "XDrishti.Application.DependencyInjection"]);
        Callers(r => r.DeclaringType?.FullName == typeof(IReadinessGaps).FullName).ShouldBe(["XDrishti.Application.Confidence.ScorecardReader", Sentinel(typeof(SecondWriterSentinel))]);

        // every answer of the source is the word unavailable with a reason: no value, never a pass
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Confidence", "UnavailableReadinessGaps.cs"));
        source.ShouldContain("new ReadinessReading<T>(null, \"unavailable: \" + reason)");
        source.ShouldNotContain("true");
        source.ShouldNotContain("Result.Success");
        typeof(IReadinessGaps).GetMethods().Length.ShouldBe(5, "rows 10 and 11 left the port when the Lab verdict gained their figures (SC-S1, ADR 0222) and row 14 when it gained the tier counts (SC-S2, ADR 0223)");
        source.Split("None<").Length.ShouldBe(5 + 2, "five answers and the helper's definition");
        typeof(IReadinessGaps).GetMethods().Select(m => m.Name).ShouldNotContain("CapacityOkAsync", "row 14 has its producer (ADR 0223) and is read from the Lab verdict by the reader, not through the gaps");
    }

    // ---- (5b) row 14: the tier and capacity counts (ADR 0223) ----

    /// <summary>Stands for a second caller of the tier computation and a reader of the stored counts that is not the scorecard's.</summary>
    internal sealed class TierConsumerSentinel(XDrishti.Application.Lab.LabTiers tiers, XDrishti.Application.Lab.CostStressView view)
    {
        public ValueTask<XDrishti.Application.Lab.CostStressTiersView?> Compute() => tiers.ComputeAsync(null, CancellationToken.None);

        public XDrishti.Application.Lab.CostStressTiersView? Read() => view.Tiers;
    }

    [Fact]
    public void The_tier_and_capacity_counts_are_computed_only_by_the_lab_runner_and_read_only_by_the_scorecard_reader()
    {
        // computed in one place, after the verdict is judged
        Callers(r => r.Name == "ComputeAsync" && r.DeclaringType?.FullName == "XDrishti.Application.Lab.LabTiers")
            .ShouldBe(["XDrishti.Application.Lab.LabRunner", Sentinel(typeof(TierConsumerSentinel))]);

        // read by the reader of row 14 and shown by the report (a display line); the verdict evaluator, the gates, the stress calculator and every chooser never name the member
        Callers(r => r.Name == "get_Tiers" && r.DeclaringType?.FullName == "XDrishti.Application.Lab.CostStressView")
            .Where(t => !t.Contains("JsonContext", StringComparison.Ordinal) && t != "XDrishti.Application.Lab.CostStressView")
            .ShouldBe(["XDrishti.Application.Confidence.LabEvidenceReader", "XDrishti.Application.Lab.LabReportBuilder", Sentinel(typeof(TierConsumerSentinel))]);

        Dependents(Application, "XDrishti.Application.Lab.LabTiers").ShouldBe(["XDrishti.Application.DependencyInjection", "XDrishti.Application.Lab.LabRunner", "XDrishti.Application.Lab.LabTiers"]);
    }

    [Fact]
    public void The_tier_and_capacity_rules_are_pure_domain_and_the_lab_tiers_read_day_bars_and_never_minute_bars()
    {
        var pure = Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Research").And().HaveNameMatching("^(LiquidityTiers|Capacity|CapacityCheck|LiquidityTier)$");

        pure.GetTypes().Count().ShouldBe(4, "control: the rule ran over the four types");
        pure.Should().NotHaveDependencyOnAny(
            "System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics.Process", "System.TimeProvider", "System.Environment", "System.Random", "System.Text.Json", "Npgsql", "Microsoft.EntityFrameworkCore",
            "XDrishti.Application", "XDrishti.Infrastructure").GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Self).That().HaveName(nameof(ClockSentinel)).Should().NotHaveDependencyOnAny("System.TimeProvider").GetResult().IsSuccessful.ShouldBeFalse(); // control

        // the daily-bar form: the only bar read of the tiers is the bounded day read
        var tiers = Callers(r => r.DeclaringType?.FullName == "XDrishti.Application.Abstractions.Market.Bars.IBarQueries" && r.Name == "ReadDailyBarsAsync");
        tiers.ShouldContain("XDrishti.Application.Lab.LabTiers");
        Callers(r => r.DeclaringType?.FullName == "XDrishti.Application.Abstractions.Market.Bars.IBarQueries" && r.Name == "ReadBarsAsync").ShouldNotContain("XDrishti.Application.Lab.LabTiers");
    }

    // ---- (6) the routes ----

    [Fact]
    public void The_routes_are_three_reads_and_the_owners_switch_with_a_fresh_password_both_ways()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "ReadinessEndpoints.cs"));

        source.Split("MapGet(").Length.ShouldBe(4, "the list, one scorecard and the settings");
        source.Split("MapPut(").Length.ShouldBe(2, "the switch");
        foreach (var verb in new[] { "MapPost", "MapDelete", "MapPatch", "MapMethods" })
        {
            source.ShouldNotContain(verb);
        }

        source.ShouldContain("/scorecards/{strategyId}/versions/{version:int}");
        source.ShouldContain("/readiness-settings");
        source.Split("OwnerAct.FreshAsync(").Length.ShouldBe(2, "one use, unconditional: a change either way needs the password (OFF removes a refusal)");
        source.ShouldNotContain("SessionOwner"); // no direction is the session alone
        source.ShouldNotContain("request.Enforce ?");
        source.ShouldNotContain("request.Actor");
        source.ShouldNotContain("request.Owner");
        Assembly.Load(new AssemblyName("XDrishti.Api")).GetType("XDrishti.Api.Endpoints.ReadinessEndpoints").ShouldNotBeNull("control: the endpoint module is the Api type the file defines");
    }

    [Fact]
    public void The_migration_is_one_added_file_and_in_a_schema_of_its_own()
    {
        var infrastructure = RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "XDrishti.Infrastructure.csproj");
        var migrations = Path.Combine(Path.GetDirectoryName(infrastructure)!, "Persistence", "Migrations");

        Directory.GetFiles(migrations, "*_AddReadiness.cs").Length.ShouldBe(1);
        Directory.GetFiles(migrations, "*_AddReadiness.Designer.cs").Length.ShouldBe(1);
        var sql = File.ReadAllText(Directory.GetFiles(migrations, "*_AddReadiness.cs")[0]);
        sql.ShouldContain("CREATE SCHEMA IF NOT EXISTS readiness");
        sql.ShouldNotContain("GrantSchemaUsageToAppRole", customMessage: "that helper grants update and delete on every table of a schema; both tables here are insert-only");
        sql.ShouldNotContain("confidence.");
        sql.ShouldNotContain("GRANT UPDATE");
        sql.ShouldNotContain("GRANT DELETE");
    }
}
