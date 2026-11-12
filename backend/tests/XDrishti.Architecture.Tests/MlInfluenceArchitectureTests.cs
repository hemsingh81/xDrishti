using System.Reflection;
using Mono.Cecil;
using NetArchTest.Rules;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Architecture.Tests;

/// <summary>
/// Spec CF-10 part 10c-2, AC9 and AC10 (ADR 0217): <c>IMlInfluenceGate</c> is the only way Planning, Selection and Autonomy may read a model's score. No type of those namespaces may depend on a scorer, the model registry or files, the score query, the predicate itself or its evidence, and
/// the one place that shows a model's figure (<c>AdvisoryPWin</c>, display only since P7) is pinned by the IL. Every scan has a sentinel, a case the same code must catch. The predicate is pure, and nothing in this version can switch influence on.
/// </summary>
public sealed class MlInfluenceArchitectureTests
{
    private static readonly Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;
    private static readonly Assembly Application = typeof(XDrishti.Application.DependencyInjection).Assembly;

    /// <summary>Every project under backend/src; a new project fails <see cref="The_list_of_production_assemblies_is_every_project_under_src"/> until it is added here, so the scans below never miss one.</summary>
    private static readonly string[] ProductionNames =
    [
        "XDrishti.Api", "XDrishti.Application", "XDrishti.Domain", "XDrishti.Execution", "XDrishti.Feed", "XDrishti.Hosting", "XDrishti.Indicators", "XDrishti.Infrastructure", "XDrishti.Learning.MlNet", "XDrishti.Mcp",
        "XDrishti.SimExport", "XDrishti.SimReader", "XDrishti.Simulation", "XDrishti.Strategies", "XDrishti.Strategies.File", "XDrishti.Worker",
    ];

    private static readonly Assembly[] Production = [.. ProductionNames.Select(n => Assembly.Load(new AssemblyName(n)))];

    /// <summary>The namespaces that decide what is ranked, sized, planned or approved (spec AC10), by assembly.</summary>
    private static readonly (Assembly Assembly, string Namespace)[] Consumers =
    [
        (Domain, "XDrishti.Domain.Selection"), (Domain, "XDrishti.Domain.Planning"), (Domain, "XDrishti.Domain.Autonomy"), (Domain, "XDrishti.Domain.Replay"),
        (Application, "XDrishti.Application.Planning"), (Application, "XDrishti.Application.Selection"), (Application, "XDrishti.Application.Autonomy"), (Application, "XDrishti.Application.Replay"), // the system replay (SR-01) decides what a replayed plan takes with the live pieces: no score source either
    ];

    // what only the gate and its evaluator may read: the predicate, its evidence and switch, the hold-out looks, the checks, the registry and its files, the trainer, the scorer loader and the score query
    private static readonly string[] GateOnly =
    [
        typeof(IModelScorerLoader).FullName!, typeof(IModelRegistry).FullName!, typeof(IModelFiles).FullName!, typeof(IModelTrainer).FullName!,
        typeof(IMlInfluenceChecks).FullName!, typeof(IMlInfluenceSwitch).FullName!, typeof(IMlInfluenceEvidence).FullName!, typeof(MlInfluence).FullName!,
        "XDrishti.Application.Learning.ScoreWithModelQuery", "XDrishti.Application.Learning.ScoreWithModelHandler", "XDrishti.Application.Learning.ModelScoreView",
        "XDrishti.Application.Learning.MlInfluenceEvaluator", "XDrishti.Application.Learning.ModelHoldOutLooks", "XDrishti.Application.Learning.MlInfluenceGate",
        "XDrishti.Application.Shadow.ShadowPlanFacts", // its AdvisoryPWin(NewPlanItem) parses the model's figure back out of a plan item
    ];

    // the records that carry the model's figure or the note it travels in; their positional Deconstruct hands the value out without naming it
    private static readonly string[] FigureRecords =
    [
        "XDrishti.Domain.Selection.PlanCandidate", "XDrishti.Application.Abstractions.Planning.PlanEvidence", "XDrishti.Application.Planning.PlanEvidenceView",
    ];

    private static bool IsFigure(MemberReference m) => m.Name is "get_AdvisoryPWin" or "AdvisoryPWin";

    private static bool IsNote(MemberReference m) => m.Name is "get_AdvisoryNote";

    private static bool IsNoteParser(MemberReference m) => m.Name is "TryReadAdvisoryNote";

    private static bool IsDeconstruct(MemberReference m) => m.Name == "Deconstruct" && FigureRecords.Contains(m.DeclaringType.FullName);

    // The model read queries (GetModelQuery / ModelDetailView, ListModelsQuery / ModelView) are not score sources and are not listed: they return the registry's record of a model (hash, verdict, the calibration table of raw score against probability, feature importance,
    // designations) and take no features, so they cannot give a probability for a candidate. Only ScoreWithModelQuery does, and it is listed.
    private static readonly string[] ScoreSources = [.. GateOnly, .. ScorerTypes()];

    private static List<string> ScorerTypes()
    {
        Type[] contracts = [typeof(IRowScorer), typeof(IScorerFactory), typeof(IExplainableScorer)];
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var contract in contracts)
        {
            found.Add(contract.FullName!);
        }

        foreach (var type in Production.SelectMany(a => a.GetTypes()))
        {
            var generated = type.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is not null;
            var calibrator = type.Namespace == "XDrishti.Domain.Learning" && !type.IsNested && type.Name.EndsWith("Calibrator", StringComparison.Ordinal);
            if (!generated && ((type is { IsClass: true } or { IsValueType: true } && contracts.Any(c => c.IsAssignableFrom(type))) || calibrator))
            {
                found.Add(type.FullName!.Replace('+', '/'));
                var root = type;
                while (root.DeclaringType is { } outer)
                {
                    root = outer;
                }

                found.Add(root.FullName!);
            }
        }

        return [.. found];
    }

    /// <summary>Stands for a decision type that reads a model's score through the scorer port.</summary>
    internal sealed class ScorerSentinel(IModelScorerLoader loader)
    {
        public object Load(byte[] bytes) => loader.Load(bytes, "x");
    }

    /// <summary>Stands for a decision type that reads a model's figure of a candidate, or the text it is carried in.</summary>
    internal static class AdvisorySentinel
    {
        public static double? Read(XDrishti.Domain.Selection.PlanCandidate candidate) => candidate.AdvisoryPWin;

        public static string? ReadNote(XDrishti.Application.Abstractions.Planning.PlanEvidence evidence) => evidence.AdvisoryNote;

        public static bool ParseNote(string? note) => XDrishti.Application.Planning.SelectionPlanBuilder.TryReadAdvisoryNote(note, out _);

        public static double? ShadowFigure(XDrishti.Application.Abstractions.Planning.NewPlanItem item) => XDrishti.Application.Shadow.ShadowPlanFacts.AdvisoryPWin(item);

        public static void Deconstruct(XDrishti.Application.Abstractions.Planning.PlanEvidence evidence) => evidence.Deconstruct(out _, out _, out _, out _, out _, out _, out _, out _, out _, out _, out _);
    }


    /// <summary>Stands for a type that reads the switch.</summary>
    internal sealed class SwitchReaderSentinel(IMlInfluenceSwitch influenceSwitch)
    {
        public ValueTask<bool> On(CancellationToken cancellationToken) => influenceSwitch.IsOnAsync(cancellationToken);
    }

    // a closure or a state machine (named <...>) belongs to the type that declares it; a nested type that someone wrote keeps its own name
    private static Type Owner(Type type)
    {
        while (type.IsNested && type.Name.StartsWith('<'))
        {
            type = type.DeclaringType!;
        }

        return type;
    }

    private static List<string> TopLevelNames(IEnumerable<Type> types) => [.. types.Select(t => Owner(t).FullName!).Distinct().Order(StringComparer.Ordinal)];

    private static List<Type> ConsumerTypes() =>
        [.. Consumers.SelectMany(c => Types.InAssembly(c.Assembly).That().ResideInNamespace(c.Namespace).GetTypes())];

    private static List<string> Implementations(IEnumerable<Assembly> assemblies, Type contract) =>
        [.. assemblies.SelectMany(a => a.GetTypes()).Where(t => t is { IsClass: true, IsAbstract: false } && contract.IsAssignableFrom(t)).Select(t => t.FullName!).Order(StringComparer.Ordinal)];

    private static List<string> Dependents(IEnumerable<Assembly> assemblies, Type contract) =>
        [.. assemblies.SelectMany(a => TopLevelNames(Types.InAssembly(a).That().HaveDependencyOn(contract.FullName!).GetTypes())).Order(StringComparer.Ordinal)];

    /// <summary>The top-level types of the assembly (under one of the namespaces, if given) whose code calls or reads a member that <paramref name="member"/> accepts, read from the IL in one pass.</summary>
    private static List<string> Readers(Assembly assembly, IEnumerable<string>? namespaces, Func<MemberReference, bool> member)
    {
        using var module = ModuleDefinition.ReadModule(assembly.Location);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in module.GetTypes())
        {
            var root = type;
            while (root.DeclaringType is not null && root.Name.StartsWith('<'))
            {
                root = root.DeclaringType;
            }

            if (namespaces is not null && !namespaces.Any(ns => root.FullName.StartsWith(ns + ".", StringComparison.Ordinal)))
            {
                continue;
            }

            if (type.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Any(i => i.Operand is MemberReference reference && member(reference)))
            {
                found.Add(root.FullName.Replace('/', '+'));
            }
        }

        return [.. found];
    }

    private static string[] ConsumerNamespaces(Assembly assembly) => [.. Consumers.Where(c => c.Assembly == assembly).Select(c => c.Namespace)];

    [Fact]
    public void The_list_of_production_assemblies_is_every_project_under_src()
    {
        var domain = RepositoryFiles.RepositoryFile("backend", "src", "XDrishti.Domain", "XDrishti.Domain.csproj");
        var projects = Directory.GetDirectories(Path.GetDirectoryName(Path.GetDirectoryName(domain))!, "XDrishti.*").Select(d => Path.GetFileName(d)!).Order(StringComparer.Ordinal).ToList();

        projects.ShouldBe(ProductionNames.Order(StringComparer.Ordinal).ToList());
        Production.Length.ShouldBe(ProductionNames.Length);
    }

    [Fact]
    public void The_score_sources_are_found_by_reflection_not_by_a_list_that_can_go_stale()
    {
        ScoreSources.ShouldContain("XDrishti.Domain.Learning.LogisticScorer");
        ScoreSources.ShouldContain("XDrishti.Domain.Learning.CellRateScorer");
        ScoreSources.ShouldContain("XDrishti.Domain.Learning.PlattCalibrator");
        ScoreSources.ShouldContain("XDrishti.Domain.Learning.IsotonicCalibrator");
        ScoreSources.ShouldContain("XDrishti.Learning.MlNet.MlNetScorerFactory");
    }

    [Fact]
    public void Planning_selection_and_autonomy_depend_on_no_score_source()
    {
        ConsumerTypes().Count.ShouldBeGreaterThan(100, "the scan sees the namespaces");
        foreach (var (assembly, ns) in Consumers)
        {
            var result = Types.InAssembly(assembly).That().ResideInNamespace(ns).Should().NotHaveDependencyOnAny(ScoreSources).GetResult();

            result.IsSuccessful.ShouldBeTrue(ns + ": " + string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void The_scan_sees_a_decision_type_that_reads_a_score_through_a_scorer_port()
    {
        // control: the same rule on a sentinel fails, and the gate and its evaluator, which do read scores, are seen too
        Types.InAssembly(typeof(ScorerSentinel).Assembly).That().HaveName(nameof(ScorerSentinel)).Should().NotHaveDependencyOnAny(ScoreSources).GetResult().IsSuccessful.ShouldBeFalse();
        Types.InAssembly(Application).That().HaveName("MlInfluenceGate").Should().HaveDependencyOnAny(ScoreSources).GetResult().IsSuccessful.ShouldBeTrue();
        Types.InAssembly(Application).That().HaveName("MlInfluenceEvaluator").Should().HaveDependencyOnAny(ScoreSources).GetResult().IsSuccessful.ShouldBeTrue();
    }

    [Fact]
    public void The_model_figure_of_a_candidate_and_the_text_it_travels_in_are_read_only_by_the_types_that_show_it()
    {
        var domain = ConsumerNamespaces(Domain);
        var application = ConsumerNamespaces(Application);

        // the figure itself (P7-00 Q8: display only): ranking, the smart rules, sizing, the fill and the automatic approval never read it, by its getter or by the plan fact that parses it back out of an item
        Readers(Domain, domain, IsFigure).ShouldBe(["XDrishti.Domain.Selection.PlanCandidate", "XDrishti.Domain.Selection.RankedCandidate"]);
        Readers(Application, application, IsFigure).ShouldBe(["XDrishti.Application.Planning.SelectionPlanBuilder"]);

        // the positional deconstruction of a candidate or of the evidence that carries the note would read the figure without naming it
        Readers(Domain, null, IsDeconstruct).ShouldBeEmpty();
        Readers(Application, null, IsDeconstruct).ShouldBeEmpty();

        // the figure also travels as the advisory note of a plan item: written by the plan builder, shown by the plan views, parsed back only by the shadow record
        Readers(Application, application, IsNote).ShouldBe(["XDrishti.Application.Planning.PlanEvidenceView", "XDrishti.Application.Planning.PlanItemView"]);
        Readers(Application, null, IsNoteParser).ShouldBe(["XDrishti.Application.Shadow.ShadowPlanFacts"]);
    }

    [Fact]
    public void The_scan_for_the_model_figure_sees_a_reader_of_each_thing_it_pins()
    {
        var sentinels = typeof(AdvisorySentinel).Assembly;
        var sentinel = typeof(AdvisorySentinel).FullName!;

        Readers(sentinels, null, IsFigure).ShouldContain(sentinel, "the getter and the shadow plan fact");
        Readers(sentinels, null, m => m.Name == "AdvisoryPWin" && m.DeclaringType.FullName == "XDrishti.Application.Shadow.ShadowPlanFacts").ShouldContain(sentinel);
        Readers(sentinels, null, IsNote).ShouldContain(sentinel);
        Readers(sentinels, null, IsNoteParser).ShouldContain(sentinel);
        Readers(sentinels, null, IsDeconstruct).ShouldContain(sentinel);
        Readers(sentinels, null, m => m.Name == "Load" && m.DeclaringType.FullName == typeof(IModelScorerLoader).FullName).ShouldContain(typeof(ScorerSentinel).FullName!);
        Types.InAssembly(sentinels).That().HaveName(nameof(AdvisorySentinel)).Should().NotHaveDependencyOnAny(ScoreSources).GetResult().IsSuccessful.ShouldBeFalse("the dependency rule sees the shadow plan fact too");
    }

    [Fact]
    public void No_decision_type_asks_the_gate_yet_a_later_slice_behind_the_switch_adds_the_first_one_here()
    {
        // pinned empty on purpose: the day a consumer is added it must be named here, in review, and it may use the gate and nothing else
        foreach (var (assembly, ns) in Consumers)
        {
            TopLevelNames(Types.InAssembly(assembly).That().ResideInNamespace(ns).And().HaveDependencyOn(typeof(IMlInfluenceGate).FullName!).GetTypes()).ShouldBeEmpty(ns);
        }

        // control: the gate port is seen where it is used
        Dependents(Production, typeof(IMlInfluenceGate)).ShouldContain("XDrishti.Application.Learning.MlInfluenceGate");
    }

    [Fact]
    public void The_only_switch_is_the_stored_one_off_with_no_row_and_only_the_evaluator_reads_it_in_any_project()
    {
        Implementations(Production, typeof(IMlInfluenceSwitch)).ShouldBe(["XDrishti.Infrastructure.Learning.MlSwitchSettings"], "one switch: the newest row of ml.influence.enabled in research.ml_switch_settings, OFF when there is none (part 10d-1, ADR 0220)");
        Dependents(Production, typeof(IMlInfluenceSwitch))
            .ShouldBe(["XDrishti.Application.Learning.MlInfluenceEvaluator", "XDrishti.Infrastructure.DependencyInjection", "XDrishti.Infrastructure.Learning.MlSwitchSettings"]);

        // controls: the same scans see a second switch and a reader of it in the test assembly
        Implementations([typeof(SwitchSentinel).Assembly], typeof(IMlInfluenceSwitch)).ShouldContain(typeof(SwitchSentinel).FullName!);
        Dependents([typeof(SwitchSentinel).Assembly], typeof(IMlInfluenceSwitch)).ShouldContain(typeof(SwitchReaderSentinel).FullName!);
    }

    [Fact]
    public void The_only_evidence_source_is_the_shadow_one_and_only_the_evaluator_reads_it_in_any_project()
    {
        Implementations(Production, typeof(IMlInfluenceEvidence)).ShouldBe(["XDrishti.Application.Learning.ShadowMlInfluenceEvidence"], "the one source: forward figures from the shadow scores, the calibration alarm from the stored monitor row; the hold-out figures stay unavailable until HO-01 (CF-10e, ADR 0281)");
        Dependents(Production, typeof(IMlInfluenceEvidence))
            .ShouldBe(["XDrishti.Application.DependencyInjection", "XDrishti.Application.Learning.MlInfluenceEvaluator", "XDrishti.Application.Learning.ShadowMlInfluenceEvidence"]);
        Implementations([typeof(MlInfluenceEvidenceSentinel).Assembly], typeof(IMlInfluenceEvidence)).ShouldContain(typeof(MlInfluenceEvidenceSentinel).FullName!);
    }

    [Fact]
    public void The_predicate_and_its_value_types_are_pure_no_clock_no_io_no_system_random()
    {
        const string Pure = "^(MlInfluence|MlInfluenceFacts|MlInfluenceResult|MlInfluenceReason|MlTermResult|MlTermState|MlHoldOutFacts|MlForwardFacts|MlInfluenceOptions)$";

        var result = Types.InAssembly(Domain).That().HaveNameMatching(Pure).Should()
            .NotHaveDependencyOnAny("System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics.Process", "System.Diagnostics.Stopwatch", "System.TimeProvider", "System.Environment", "System.Random", "Npgsql", "Microsoft.EntityFrameworkCore", "XDrishti.Application", "XDrishti.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Domain).That().HaveNameMatching(Pure).GetTypes().Count().ShouldBe(9);
    }

    [Fact]
    public void The_influence_code_places_no_order_and_names_no_broker_or_execution_type()
    {
        string[] banned = ["XDrishti.Execution", "XDrishti.Application.Execution", "XDrishti.Application.LiveTrading", "XDrishti.Application.Abstractions.Brokers", "XDrishti.Infrastructure.Brokers"];
        const string Influence = "^(MlInfluence.*|GetMlInfluence.*|NoMlInfluenceEvidence|ModelHoldOutLooks|HoldOutReading|MlGatedScore|MlHoldOutFigures|IMlInfluence.*)$";

        var result = Types.InAssembly(Application).That().HaveNameMatching(Influence).Should().NotHaveDependencyOnAny(banned).GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
        Types.InAssembly(Application).That().HaveNameMatching(Influence).GetTypes().Count().ShouldBeGreaterThan(10);
    }
}
