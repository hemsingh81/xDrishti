using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Confidence;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-12 part 12a, AC12 (ADR 0218): the decay monitor, the kill-rule evaluations, the three-arm loop test, the learning curve, the reliability slope and the decision lift are pure Domain calculators; nothing that chooses what to approve (Planning, Selection,
/// Autonomy) or moves a cell (the lifecycle) can read them, their handlers or their evidence port, and **no consumer exists yet** (pinned empty: the first consumer must be added to this test in review); the handlers only read; the one evidence source says nothing is
/// available; the routes are GETs; and the P6-09 and P6-12 modules and the paired bootstrap name none of the new types (their outputs are also pinned by <c>Cf12GoldenTests</c>). Every scan has a positive control and a sentinel it must catch.
/// </summary>
public sealed class DecayLoopArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    // every Domain calculator type of the slice by simple name (options are settings and are checked below)
    private static readonly string[] CalculatorNames =
    [
        nameof(DecayMonitor), nameof(DecayTrade), nameof(DecayWindow), nameof(DecayQuarter), nameof(DecayReport),
        nameof(KillRules), nameof(KillRuleVerdict), nameof(KillRuleState), nameof(KillDecision),
        nameof(ThreeArmLoopTest), nameof(ThreeArmResult), nameof(ArmComparison), nameof(LoopCandidate), nameof(LoopVerdict),
        nameof(LearningCurve), nameof(LearningCurvePoint), nameof(CurveResult), nameof(CurveVerdict),
        nameof(ReliabilitySlope), nameof(ReliabilityResult), nameof(ReliabilityState),
        nameof(DecisionLift), nameof(LiftCandidate), nameof(LiftResult),
    ];

    private static readonly string[] CalculatorTypes = [.. CalculatorNames.Select(n => "XDrishti.Domain.Learning." + n)];

    // what a consumer must not name: the calculators, the settings, the handlers and their views, the evidence port and its one source
    private static readonly string[] Cf12Types =
    [
        .. CalculatorTypes, typeof(DecayOptions).FullName!, typeof(LoopOptions).FullName!, typeof(KillOptions).FullName!,
        "XDrishti.Application.Confidence.GetDecayReportHandler", "XDrishti.Application.Confidence.GetDecayReportQuery", "XDrishti.Application.Confidence.DecayReportView",
        "XDrishti.Application.Confidence.GetLoopReportHandler", "XDrishti.Application.Confidence.GetLoopReportQuery", "XDrishti.Application.Confidence.LoopReportView",
        "XDrishti.Application.Confidence.NoLoopEvidence", typeof(ILoopEvidence).FullName!, typeof(LoopEvidence).FullName!, typeof(LoopArmsInput).FullName!,
    ];

    // the series rules the sequential test and the decay report both read, so that they cannot read different series; a chooser names neither
    private static readonly string[] SharedSeries =
    [
        "XDrishti.Application.Shadow.EdgeSeries", "XDrishti.Application.Shadow.EdgeSelection", "XDrishti.Application.Shadow.EdgeFigure", "XDrishti.Application.Shadow.EdgeFigures",
        "XDrishti.Application.Shadow.BehaviouralSeries", "XDrishti.Application.Shadow.BehaviouralFigure",
    ];

    private static readonly (Assembly Assembly, string Namespace)[] Choosers =
    [
        (Domain, "XDrishti.Domain.Selection"), (Domain, "XDrishti.Domain.Planning"), (Domain, "XDrishti.Domain.Autonomy"),
        (Application, "XDrishti.Application.Planning"), (Application, "XDrishti.Application.Selection"), (Application, "XDrishti.Application.Autonomy"),
    ];

    /// <summary>Stands for a decision type that reads a calculator.</summary>
    internal sealed class CalculatorSentinel
    {
        public static bool Read(IReadOnlyList<DecayTrade> series, ExpectancyBand band) => DecayMonitor.Evaluate(series, band, new DecayOptions(), 0.3, new DriftOptions()).TwiceInARow;

        public static KillRuleState Kill() => KillRules.Drawdown([], 1).State;
    }

    /// <summary>Stands for a second source of loop evidence.</summary>
    internal sealed class EvidenceSentinel : ILoopEvidence
    {
        public ValueTask<LoopEvidence> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(new LoopEvidence(null, "x", null, "x", null, "x", null, "x"));
    }

    [Fact]
    public void The_calculators_are_pure_no_clock_no_io_no_system_random_and_name_no_application_type()
    {
        var pure = Types.InAssembly(Domain).That().HaveNameMatching("^(" + string.Join('|', CalculatorNames) + ")$");

        var result = pure.Should().NotHaveDependencyOnAny(
            "System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics.Process", "System.Diagnostics.Stopwatch", "System.Diagnostics.Debug", "System.Diagnostics.Activity", "System.TimeProvider",
            "System.Environment", "System.Random", "System.Security.Cryptography", "System.Text.Json", "Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Application", "XDrishti.Infrastructure").GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        pure.GetTypes().Count().ShouldBe(CalculatorNames.Length, "control: the list is the set of types the rule ran over");
        foreach (var name in new[] { "DecayMonitor", "KillRules", "ThreeArmLoopTest", "LearningCurve", "ReliabilitySlope", "DecisionLift" })
        {
            var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "Learning", name + ".cs"));
            source.ShouldNotContain("DateTime", customMessage: name);
            source.ShouldNotContain("new Random", customMessage: name);
            source.ShouldNotContain("Random.Shared", customMessage: name);
        }

        // control: the rule sees a forbidden dependency where there is one (the sentinel reads the calculators and the Application layer's namespace is named in the list)
        Types.InAssembly(typeof(DecayLoopArchitectureTests).Assembly).That().HaveName(nameof(CalculatorSentinel)).Should().HaveDependencyOnAny(CalculatorTypes).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_slice_reuses_the_existing_cusum_and_paired_bootstrap_and_does_not_copy_them()
    {
        Types.InAssembly(Domain).That().HaveName(nameof(DecayMonitor)).Should().HaveDependencyOn(typeof(CusumDetector).FullName!).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Domain).That().HaveName(nameof(DecayMonitor)).Should().HaveDependencyOn(typeof(RollingBandDetector).FullName!).GetResult().IsSuccessful.ShouldBeFalse("the band maker is the application's concern; the monitor takes the band");
        Types.InAssembly(Domain).That().HaveName(nameof(ThreeArmLoopTest)).Should().HaveDependencyOn(typeof(PairedDayBootstrap).FullName!).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Domain).That().HaveName(nameof(ReliabilitySlope)).Should().HaveDependencyOn("XDrishti.Domain.Learning.DriftSeries").GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Planning_selection_and_autonomy_name_nothing_of_the_slice()
    {
        foreach (var (assembly, ns) in Choosers)
        {
            var result = Types.InAssembly(assembly).That().ResideInNamespace(ns).Should().NotHaveDependencyOnAny([.. Cf12Types, .. SharedSeries]).GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }

        // control: the namespaces exist and hold types, and the same rule catches a reader
        foreach (var (assembly, ns) in Choosers)
        {
            Types.InAssembly(assembly).That().ResideInNamespace(ns).GetTypes().ShouldNotBeEmpty(ns);
        }

        Types.InAssembly(typeof(DecayLoopArchitectureTests).Assembly).That().HaveName(nameof(CalculatorSentinel)).Should().NotHaveDependencyOnAny(Cf12Types).GetResult().IsSuccessful.ShouldBeFalse();
    }

    [Fact]
    public void The_two_series_handlers_read_the_one_shared_series_rule_and_nothing_else_does()
    {
        foreach (var name in SharedSeries)
        {
            Application.GetType(name).ShouldNotBeNull(name);
        }

        var readers = Types.InAssembly(Application).That().HaveDependencyOnAny(SharedSeries).GetTypes()
            .Select(t => t.Name.Split('`')[0]).Where(n => !n.StartsWith('<') && !SharedSeries.Any(f => f.EndsWith("." + n, StringComparison.Ordinal))).Distinct().Order(StringComparer.Ordinal).ToList();

        // CF-11 part 1 adds one reader, the readiness scorecard's forward-edge reader: it reads the same series through the same rule (and no chooser may name it: ReadinessArchitectureTests)
        readers.ShouldBe(["ForwardEdgeReader", "GetDecayReportHandler", "GetSequentialStatusHandler"]);
    }

    [Fact]
    public void The_lifecycle_and_cell_state_moves_name_nothing_of_the_slice()
    {
        // the lifecycle (P6-08) and the cell-state writer never read a decay, kill-rule or loop calculator: the pause on a kill rule comes from the Confidence namespace through the pause handler (KillArchitectureTests), carrying only text and evidence ids
        var lifecycle = Types.InAssembly(Domain).That().HaveNameMatching("^(CellLifecycle|CellMove|CellState|CellStateExtensions|CellWindowExtensions|HypothesisLifecycle|LessonLifecycle)$").GetTypes().ToList();
        lifecycle.ShouldNotBeEmpty();
        Types.InAssembly(Domain).That().HaveNameMatching("^(CellLifecycle|CellMove|CellState|CellStateExtensions|CellWindowExtensions|HypothesisLifecycle|LessonLifecycle)$").Should().NotHaveDependencyOnAny(Cf12Types).GetResult().IsSuccessful.ShouldBeTrue();

        var movers = Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Learning").And().HaveNameMatching("^(RecordCellTransition|GetCellState|ComputeCellStats|Replay).*").GetTypes().ToList();
        movers.ShouldNotBeEmpty();
        Types.InAssembly(Application).That().ResideInNamespace("XDrishti.Application.Learning").Should().NotHaveDependencyOnAny(Cf12Types).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void Nothing_outside_the_slice_reads_it_yet_the_consumers_are_pinned_empty()
    {
        // Application: only the slice's own namespace and the explicit registration read the types
        var users = Types.InAssembly(Application).That().HaveDependencyOnAny(Cf12Types).GetTypes()
            .Select(t => (t.Namespace ?? string.Empty, Name: t.Name.Split('`')[0]))
            .Where(t => t.Item1 is not ("XDrishti.Application.Confidence" or "XDrishti.Application.Abstractions.Confidence") && !t.Name.StartsWith('<'))
            .Select(t => t.Name)
            .Distinct().Order(StringComparer.Ordinal).ToList();

        // SR-01 part D (ADR 0272) is the one consumer outside the slice: the research-only system-replay pair runner passes LoopOptions to the
        // frozen-versus-learning comparison; it is fenced from evidence and decisions (ReplayArchitectureTests)
        users.ShouldBe(["DependencyInjection", "ReplayPairRunner"]);

        // Domain: no type outside the slice's calculators names a calculator, and the P6-09 and P6-12 modules and the shared bootstrap name none of the slice's types
        // SR-01 part D (ADR 0272): the research-only replay pair comparison reads ThreeArmLoopTest, so the paired rule is the loop test's own
        Types.InAssembly(Domain).That().DoNotHaveNameMatching("^(" + string.Join('|', CalculatorNames) + "|ReplayPairComparison)$").Should().NotHaveDependencyOnAny(CalculatorTypes).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Domain).That().HaveName("ReplayPairComparison").Should().HaveDependencyOn(typeof(ThreeArmLoopTest).FullName!).GetResult().IsSuccessful.ShouldBeTrue();
        const string P6 = "^(CusumDetector|RollingBandDetector|CalibrationDriftDetector|CounterfactualEvaluator|DriftSeries|DriftReplay|PairedDayBootstrap|PairedBootstrapResult|MonteCarlo|BacktestBand)$";
        var frozen = Types.InAssembly(Domain).That().HaveNameMatching(P6);
        frozen.GetTypes().Count().ShouldBe(10);
        frozen.Should().NotHaveDependencyOnAny(Cf12Types).GetResult().IsSuccessful.ShouldBeTrue();

        // control: the scan finds the real readers (the handler reads the calculators)
        Types.InAssembly(Application).That().HaveName("GetDecayReportHandler").Should().HaveDependencyOnAny(CalculatorTypes).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_handlers_only_read_no_store_no_writer_no_clock_no_logger_and_no_command()
    {
        foreach (var handler in new[] { "GetDecayReportHandler", "GetLoopReportHandler" })
        {
            var result = Types.InAssembly(Application).That().HaveName(handler).Should().NotHaveDependencyOnAny(
                "XDrishti.Application.Abstractions.Shadow.IShadowStore", "XDrishti.Application.Abstractions.Shadow.IShadowGradeStore", "XDrishti.Application.Abstractions.Shadow.IShadowSwitchStore", "XDrishti.Application.Abstractions.Shadow.IShadowSettings",
                "XDrishti.Application.Abstractions.Confidence.IKillProposalStore", "XDrishti.Application.Abstractions.Confidence.IKillSwitchStore", "XDrishti.Application.Abstractions.Confidence.IDecayCheckStore", "XDrishti.Application.Abstractions.Confidence.IKillSettings",
                "XDrishti.Application.Abstractions.Learning.ILearningChangelog", "XDrishti.Application.Abstractions.Learning.ICellStatsStore", "XDrishti.Application.Abstractions.Learning.IModelRegistry", "XDrishti.Application.Abstractions.Paper.IPaperStore",
                "XDrishti.Application.Abstractions.Paper.IGovernanceStore", "XDrishti.Application.Abstractions.Lab.IHoldOutStore", "XDrishti.Application.Planning", "XDrishti.Application.Autonomy", "XDrishti.Application.Abstractions.Execution",
                "XDrishti.Application.Abstractions.Messaging.ICommandHandler", "System.TimeProvider", "Microsoft.Extensions.Logging").GetResult();

            result.IsSuccessful.ShouldBeTrue(handler + ": " + string.Join(", ", result.FailingTypeNames ?? []));
            Types.InAssembly(Application).That().HaveName(handler).GetTypes().ShouldHaveSingleItem();
            var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Application", "Confidence", handler + ".cs"));
            source.ShouldNotContain("ICommandHandler");
            source.ShouldNotContain("AppendAsync");
            source.ShouldNotContain("InsertAsync");
            source.ShouldNotContain("SaveChanges");
        }
    }

    [Fact]
    public void The_one_loop_evidence_source_says_nothing_is_available_and_only_the_loop_handler_reads_the_port()
    {
        static List<string> Implementations(params Assembly[] assemblies) =>
            [.. assemblies.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ILoopEvidence).IsAssignableFrom(t)).Select(t => t.FullName!).Order(StringComparer.Ordinal)];

        string[] production = ["XDrishti.Api", "XDrishti.Application", "XDrishti.Domain", "XDrishti.Execution", "XDrishti.Feed", "XDrishti.Hosting", "XDrishti.Indicators", "XDrishti.Infrastructure", "XDrishti.Learning.MlNet", "XDrishti.Mcp", "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Worker"];
        var assemblies = production.Select(n => Assembly.Load(new AssemblyName(n))).ToArray();

        Implementations(assemblies).ShouldBe(["XDrishti.Application.Confidence.NoLoopEvidence"]);
        Implementations([.. assemblies, typeof(DecayLoopArchitectureTests).Assembly]).ShouldContain(typeof(EvidenceSentinel).FullName!); // control: a second source is caught by the same scan

        var readers = Types.InAssembly(Application).That().HaveDependencyOn(typeof(ILoopEvidence).FullName!).GetTypes()
            .Select(t => t.Name.Split('`')[0]).Where(n => !n.StartsWith('<')).Distinct().Order(StringComparer.Ordinal).ToList();
        readers.ShouldBe(["DependencyInjection", "GetLoopReportHandler", "NoLoopEvidence"]);
    }

    [Fact]
    public void The_read_routes_only_read_and_the_kill_writers_are_the_ones_pinned_in_the_kill_rules()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "ConfidenceEndpoints.cs"));

        source.ShouldContain("MapGet");
        foreach (var verb in new[] { "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods" })
        {
            source.ShouldNotContain(verb);
        }

        // part 12b owns the proposals, the owner's switch and the migration (KillArchitectureTests pins who writes and who reads them); this endpoint file stays the two reads of part 12a
        var names = Types.InAssembly(Application).That().HaveNameMatching("^.*(KillProposal|ApplyKill|ProposeKill|ReEnable).*$").GetTypes().Where(t => !t.Name.StartsWith('<')).Select(t => t.Name).Order(StringComparer.Ordinal).ToList();
        names.ShouldBe(["ApplyKillCommand", "ApplyKillHandler", "ApplyKillOutcome", "GetKillProposalsHandler", "GetKillProposalsQuery", "IKillProposalReader", "IKillProposalStore", "KillProposalNotices", "KillProposalRow", "KillProposalView", "KillProposalsView", "NewKillProposal", "ProposeKillCommand", "ProposeKillHandler", "ProposeKillOutcome", "ReEnableCellCommand", "ReEnableCellHandler", "ReEnableOutcome"]);
    }

    [Fact]
    public void The_cli_verbs_only_read_through_the_query_handlers_and_write_nothing()
    {
        // part 12c-1 and CF-10 part 10d-2a: xd learn decay|loop|curve|influence|rollback call the report handlers, the proposal lists and the influence view and nothing that writes, reads the clock, reaches the database layer or borrows another
        // verb. The check reads the IL of the types of those verbs
        // and of everything nested in them (the state machines of the async methods and of the iterators, where the calls are): every type named by a call, a generic argument, a typeof, a field, a parameter or a local. NetArchTest's
        // dependency rule did not see a command handler resolved with GetService(typeof(...)) inside an async method (the sentinel below passed it), so the scan is Cecil's. A name matches by prefix, so `...Messaging.ICommandHandler` covers both generic arities.
        var api = Assembly.Load(new AssemblyName("XDrishti.Api"));
        const string Verbs = "^Learn(Decay|Loop|Influence|Rollback)(Command|Options)$|^LearnReportText$|^ReadinessCli(Command|Options)$"; // CF-11 part 2a adds xd readiness to the same rule

        var named = TypesNamedBy(api, Verbs);

        Forbidden(named).ShouldBeEmpty();

        // controls: the scan reaches the verbs' real reads inside their state machines, and the same rule flags a sentinel that holds an ICommandHandler<,> (in a state machine, resolved by typeof), one that holds the clock and one that holds a switch port
        named.ShouldContain("XDrishti.Application.Confidence.GetDecayReportQuery");
        named.ShouldContain("XDrishti.Application.Confidence.GetKillProposalsQuery");
        named.ShouldContain("XDrishti.Application.Confidence.GetLoopReportQuery");
        named.ShouldContain("XDrishti.Application.Learning.GetMlInfluenceQuery");
        named.ShouldContain("XDrishti.Application.Learning.GetRollbackProposalsQuery");
        named.ShouldContain("XDrishti.Application.Abstractions.Messaging.IQueryHandler`2");
        TypesNamedBy(api, "^LearnInfluenceCommand$").ShouldContain("XDrishti.Application.Learning.GetMlInfluenceQuery");
        TypesNamedBy(api, "^LearnRollbackCommand$").ShouldContain("XDrishti.Application.Learning.GetRollbackProposalsQuery");
        TypesNamedBy(api, "^ReadinessCliCommand$").ShouldContain("XDrishti.Application.Confidence.GetScorecardQuery");
        TypesNamedBy(api, "^ReadinessCliCommand$").ShouldContain("XDrishti.Application.Confidence.ListScorecardsQuery");

        var self = typeof(DecayLoopArchitectureTests).Assembly;
        Forbidden(TypesNamedBy(self, "^CliCommandHandlerSentinel$")).ShouldNotBeEmpty();
        Forbidden(TypesNamedBy(self, "^CliClockSentinel$")).ShouldNotBeEmpty();
        Forbidden(TypesNamedBy(self, "^CliSwitchPortSentinel$")).ShouldNotBeEmpty();
        Forbidden(TypesNamedBy(self, "^CliRollbackStoreSentinel$")).ShouldContain("XDrishti.Application.Abstractions.Learning.IRollbackProposalStore");
        Forbidden(TypesNamedBy(self, "^CliReadinessSwitchSentinel$")).ShouldContain("XDrishti.Application.Abstractions.Confidence.IReadinessSwitchStore"); // a Store port, found as a class by its name
        Forbidden(TypesNamedBy(self, "^CliReadinessGateSentinel$")).ShouldContain("XDrishti.Application.Abstractions.Confidence.IReadinessGate"); // the gate is the lifecycle's, never a verb's
        Forbidden(TypesNamedBy(self, "^CliReadinessSettingsSentinel$")).ShouldContain("XDrishti.Application.Abstractions.Confidence.IReadinessSettings"); // the switch is read through the use case
        WriterPorts.ShouldContain("XDrishti.Application.Abstractions.Confidence.IReadinessSwitchStore");
        WriterPorts.ShouldContain("XDrishti.Application.Abstractions.Confidence.IReadinessSnapshotStore");

        // the class of writer ports is found by name (control: it holds the ones that matter and many more) and each real verb, the 12c-1 verbs included, passes the same rule on its own and names something
        WriterPorts.ShouldContain("XDrishti.Application.Abstractions.Learning.IRollbackProposalStore");
        WriterPorts.ShouldContain("XDrishti.Application.Abstractions.Learning.IModelRegistry");
        WriterPorts.ShouldContain("XDrishti.Application.Abstractions.Confidence.IKillProposalStore");
        WriterPorts.Length.ShouldBeGreaterThan(30);
        foreach (var verb in new[] { "LearnDecayCommand", "LearnLoopCommand", "LearnInfluenceCommand", "LearnRollbackCommand", "ReadinessCliCommand" })
        {
            var verbNames = TypesNamedBy(api, "^" + verb + "$");
            verbNames.ShouldContain("XDrishti.Application.Abstractions.Messaging.IQueryHandler`2", verb);
            Forbidden(verbNames).ShouldBeEmpty(verb);
        }
        Forbidden(["XDrishti.Api.Cli.LearnDesignateCommand"]).ShouldNotBeEmpty(); // LearnDesignateCommand is internal to the Api, so the control is the rule's own list
    }

    // every writer port is forbidden as a class, not a hand-picked few: any interface under XDrishti.Application.Abstractions whose name ends in Store or Registry (they are the ports that write or hold state), the switch ports and the proposal reader.
    // The verbs read only through the query handlers.
    private static string[] CliForbidden =>
    [
        "XDrishti.Application.Abstractions.Messaging.ICommandHandler", "System.TimeProvider", "XDrishti.Infrastructure", "XDrishti.Api.Cli.LearnLifecycleCommand", "XDrishti.Api.Cli.LearnDesignateCommand",
        "XDrishti.Application.Abstractions.Learning.IMlSwitchSettings", "XDrishti.Application.Abstractions.Learning.IMlInfluenceSwitch", "XDrishti.Application.Abstractions.Learning.IRollbackProposalReader",
        "XDrishti.Application.Abstractions.Confidence.IReadinessGate", "XDrishti.Application.Abstractions.Confidence.IReadinessSettings", "XDrishti.Application.Abstractions.Confidence.IReadinessSnapshotReader", "XDrishti.Application.Abstractions.Confidence.IReadinessGaps",
        .. WriterPorts,
    ];

    private static string[] WriterPorts =>
    [
        .. Application.GetTypes()
            .Where(t => t.IsInterface && t.Namespace is { } ns && ns.StartsWith("XDrishti.Application.Abstractions", StringComparison.Ordinal))
            .Where(t => t.Name.Split('`')[0] is { } n && (n.EndsWith("Store", StringComparison.Ordinal) || n.EndsWith("Registry", StringComparison.Ordinal)))
            .Select(t => t.FullName!.Split('`')[0]).Distinct().Order(StringComparer.Ordinal),
    ];

    private static List<string> Forbidden(IEnumerable<string> names) =>
        [.. names.Where(n => CliForbidden.Any(f => n.StartsWith(f, StringComparison.Ordinal))).Order(StringComparer.Ordinal)];

    /// <summary>The full names of every type that the code and the signatures of the matching types (and of the types nested in them) name, read from the IL.</summary>
    private static HashSet<string> TypesNamedBy(Assembly assembly, string pattern)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);

        void Add(TypeReference? type)
        {
            if (type is null)
            {
                return;
            }

            found.Add(type.FullName);
            if (type is GenericInstanceType generic)
            {
                foreach (var argument in generic.GenericArguments)
                {
                    Add(argument);
                }
            }

            if (type is TypeSpecification specification)
            {
                Add(specification.ElementType);
            }
        }

        void AddMethod(MethodReference method)
        {
            Add(method.DeclaringType);
            Add(method.ReturnType);
            foreach (var parameter in method.Parameters)
            {
                Add(parameter.ParameterType);
            }

            if (method is GenericInstanceMethod instance)
            {
                foreach (var argument in instance.GenericArguments)
                {
                    Add(argument);
                }
            }
        }

        using var module = ModuleDefinition.ReadModule(assembly.Location);
        foreach (var type in module.GetTypes())
        {
            // a type matches when it, or a type it is nested in, has the name
            var matches = false;
            for (var t = type; t is not null; t = t.DeclaringType)
            {
                matches |= System.Text.RegularExpressions.Regex.IsMatch(t.Name, pattern);
            }

            if (!matches)
            {
                continue;
            }

            Add(type.BaseType);
            foreach (var @interface in type.Interfaces)
            {
                Add(@interface.InterfaceType);
            }

            foreach (var field in type.Fields)
            {
                Add(field.FieldType);
            }

            foreach (var method in type.Methods)
            {
                AddMethod(method);
                if (!method.HasBody)
                {
                    continue;
                }

                foreach (var variable in method.Body.Variables)
                {
                    Add(variable.VariableType);
                }

                foreach (var operand in method.Body.Instructions.Select(i => i.Operand))
                {
                    switch (operand)
                    {
                        case TypeReference reference:
                            Add(reference);
                            break;
                        case MethodReference call:
                            AddMethod(call);
                            break;
                        case FieldReference field:
                            Add(field.DeclaringType);
                            Add(field.FieldType);
                            break;
                    }
                }
            }
        }

        return found;
    }

    /// <summary>Stands for a verb that writes: it resolves a command handler in an async method (so the call sits in a state machine).</summary>
    internal sealed class CliCommandHandlerSentinel(IServiceProvider services)
    {
        public async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var handler = (ICommandHandler<ApplyKillCommand, ApplyKillOutcome>)services.GetService(typeof(ICommandHandler<ApplyKillCommand, ApplyKillOutcome>))!;
            await Task.Yield();
            return handler is not null && !cancellationToken.IsCancellationRequested;
        }
    }

    /// <summary>Stands for a verb that reads the switches through their port instead of through the query handler.</summary>
    internal sealed class CliSwitchPortSentinel(XDrishti.Application.Abstractions.Learning.IMlSwitchSettings switches)
    {
        public async Task<bool> OnAsync(CancellationToken cancellationToken) => (await switches.CurrentAsync(cancellationToken)).Influence.On;
    }

    /// <summary>Stands for a verb that holds the rollback proposal store (a writer port).</summary>
    internal sealed class CliRollbackStoreSentinel(XDrishti.Application.Abstractions.Learning.IRollbackProposalStore store)
    {
        public XDrishti.Application.Abstractions.Learning.IRollbackProposalStore Store => store;
    }

    /// <summary>Stands for the readiness verb holding the owner's switch store (a writer port).</summary>
    internal sealed class CliReadinessSwitchSentinel(XDrishti.Application.Abstractions.Confidence.IReadinessSwitchStore store)
    {
        public XDrishti.Application.Abstractions.Confidence.IReadinessSwitchStore Store => store;
    }

    /// <summary>Stands for the readiness verb asking the lifecycle's gate.</summary>
    internal sealed class CliReadinessGateSentinel(XDrishti.Application.Abstractions.Confidence.IReadinessGate gate)
    {
        public XDrishti.Application.Abstractions.Confidence.IReadinessGate Gate => gate;
    }

    /// <summary>Stands for the readiness verb reading the enforce switch through its port.</summary>
    internal sealed class CliReadinessSettingsSentinel(XDrishti.Application.Abstractions.Confidence.IReadinessSettings settings)
    {
        public async Task<bool> OnAsync(CancellationToken cancellationToken) => (await settings.CurrentAsync(cancellationToken)).Enforce;
    }

    /// <summary>Stands for a verb that reads the clock.</summary>
    internal sealed class CliClockSentinel(TimeProvider clock)
    {
        public DateTimeOffset Now() => clock.GetUtcNow();
    }
}
