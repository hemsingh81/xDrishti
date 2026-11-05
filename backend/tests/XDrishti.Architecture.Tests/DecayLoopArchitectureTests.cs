using System.Reflection;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Confidence;
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
        nameof(KillRules), nameof(KillRuleVerdict), nameof(KillRuleState),
        nameof(ThreeArmLoopTest), nameof(ThreeArmResult), nameof(ArmComparison), nameof(LoopCandidate), nameof(LoopVerdict),
        nameof(LearningCurve), nameof(LearningCurvePoint), nameof(CurveResult), nameof(CurveVerdict),
        nameof(ReliabilitySlope), nameof(ReliabilityResult), nameof(ReliabilityState),
        nameof(DecisionLift), nameof(LiftCandidate), nameof(LiftResult),
    ];

    private static readonly string[] CalculatorTypes = [.. CalculatorNames.Select(n => "XDrishti.Domain.Learning." + n)];

    // what a consumer must not name: the calculators, the settings, the handlers and their views, the evidence port and its one source
    private static readonly string[] Cf12Types =
    [
        .. CalculatorTypes, typeof(DecayOptions).FullName!, typeof(LoopOptions).FullName!,
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

        readers.ShouldBe(["GetDecayReportHandler", "GetSequentialStatusHandler"]);
    }

    [Fact]
    public void The_lifecycle_and_cell_state_moves_name_nothing_of_the_slice()
    {
        // nothing in this part may move a cell: the lifecycle (P6-08) and the cell-state writer never read a decay or loop type; a pause on a decay alert is part 12b and must be added here in review
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

        users.ShouldBe(["DependencyInjection"]);

        // Domain: no type outside the slice's calculators names a calculator, and the P6-09 and P6-12 modules and the shared bootstrap name none of the slice's types
        Types.InAssembly(Domain).That().DoNotHaveNameMatching("^(" + string.Join('|', CalculatorNames) + ")$").Should().NotHaveDependencyOnAny(CalculatorTypes).GetResult().IsSuccessful.ShouldBeTrue();
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
    public void The_routes_only_read_and_the_slice_adds_no_kill_setting_proposal_or_migration()
    {
        var source = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Endpoints", "ConfidenceEndpoints.cs"));

        source.ShouldContain("MapGet");
        foreach (var verb in new[] { "MapPost", "MapPut", "MapDelete", "MapPatch", "MapMethods" })
        {
            source.ShouldNotContain(verb);
        }

        // part 12b owns the proposals, the auto setting and the migration; nothing of them exists yet
        var settings = File.ReadAllText(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Api", "Confidence", "ConfidenceSettings.cs"));
        settings.ShouldNotContain("Kill");
        Directory.GetFiles(Path.Combine(Path.GetDirectoryName(RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Infrastructure", "Persistence", "XDrishtiDbContext.cs"))!, "Migrations"), "*Decay*").ShouldBeEmpty();
        Types.InAssembly(Application).That().HaveNameMatching("^.*(KillProposal|ApplyKill|ProposeKill|ReEnable).*$").GetTypes().ShouldBeEmpty();
    }
}
