namespace XDrishti.Architecture.Tests;

/// <summary>
/// What the research family adds to the Domain stays small and known (P3-02b, ADR 0036; P3-03a): the public types of <c>XDrishti.Domain.Research</c> are listed here, so that a type added to the Domain by a slice of the simulator is
/// a decision someone made and not a side effect. The family is shared by the strategy model (<c>Side</c>), the scanner (<c>ScanCounters</c>, <c>ConditionCounter</c>) and, once merged, the walker (<c>WalkCounters</c>, <c>SimTrade</c>
/// and their enums): each of those slices adds its names to this list in its own change. No other namespace of the Domain gains a type for them.
/// </summary>
public sealed class DomainResearchSurfaceTests
{
    private static readonly System.Reflection.Assembly Domain = typeof(XDrishti.Domain.Common.Result).Assembly;

    private static readonly string[] Namespaces =
    [
        "XDrishti.Domain.Accounts", "XDrishti.Domain.Allocation", "XDrishti.Domain.Assistant", "XDrishti.Domain.Audit", "XDrishti.Domain.Auth", "XDrishti.Domain.Autonomy", "XDrishti.Domain.Capabilities", "XDrishti.Domain.Checklist", "XDrishti.Domain.Common", "XDrishti.Domain.Lab", "XDrishti.Domain.Learning", "XDrishti.Domain.Market", "XDrishti.Domain.Market.Actions", "XDrishti.Domain.Market.Bars", "XDrishti.Domain.Market.Baskets",
        "XDrishti.Domain.Market.Calendar", "XDrishti.Domain.Market.Context", "XDrishti.Domain.Market.Quality", "XDrishti.Domain.Paper", "XDrishti.Domain.Planning", "XDrishti.Domain.Platform", "XDrishti.Domain.Platform.Services", "XDrishti.Domain.Portfolio", "XDrishti.Domain.Reporting", "XDrishti.Domain.Research", "XDrishti.Domain.Selection", "XDrishti.Domain.Selection.SmartRules",
    ];

    [Fact]
    public void The_public_types_of_the_research_namespace_are_exactly_the_known_ones()
    {
        var research = Domain.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Domain.Research").Select(t => t.Name).Order().ToList();

        research.ShouldBe(
        [
            "AppliedAdjustment", "BarStreamHash", "BarrierOutcome", "BuiltInParameterInfo", "BuiltInStrategyInfo", "CalendarExceptionDay", "CandidateDiagnostics", "CandidateRecord", "CandidateRun", "CanonicalText", "ChargeRates", "ChargeRounding", "ChargeSchedule",
            "ComparisonReplay", "ComparisonReport", "ComparisonRow", "ComparisonRun", "ConditionCounter", "ConditionItemTrace", "ConditionSectionTrace", "CostApplication",
            "CostBasis", "CostContext", "CostDescription", "CostErrors", "CostLimits", "CostModel", "CostRecord", "CostSegment", "CostSheet", "CostSheetJson", "DateSpan", "DiagnosticFigure", "DiscontinuityNote", "EntryFill", "ExitCandidate", "ExitChoice", "ExitFlags", "ExitParameterValue", "ExitReason", "ExitSelection", "ExitSelector", "ExitShare", "ExplainRequest", "ExplainResult", "FeatureReadTrace", "FillBasis",
            "FillDescription", "FillLimit", "FillLimits", "FillRules", "FoldCandidateRow", "GapFillRule", "InstrumentReplay", "InstrumentReplayInput", "InstrumentRun", "LabelBasis", "Leg", "LegCharge", "LegCharges", "LegKind", "NoTradeReason", "PairedFigure", "PlateauChoice", "PooledFigure", "ReplayOptions", "RunAssembler", "RunCounters",
            "RunFates", "RunHeader", "RunLabelSet", "RunLabels", "RunTrade", "SampleFigures", "ScanCounters", "SelectionFold", "SelectionInput", "SelectionOptions", "SelectionStability", "SelectionTrade", "SessionCounts", "SessionExclusion", "SessionExclusions", "SessionNote", "Side", "SimTrade", "SimTradeDifference", "SimulationErrors", "SimulationRun",
            "Sizing", "SizingProfile", "SkipCount", "SkipReason", "SkipReasons", "SlippageConfig", "StrategyRecord", "TickProbe", "TickSpec", "TickUnitCheck", "TimeStopChoice", "TradeCardData", "TradeCostApplier", "TradeCosting", "TradeExplanation", "TradeSkip", "TrainingSet", "TrainingSetBuilder", "TripleBarrier", "TripleBarrierLabel", "TruthState", "UniverseKind",
            "UniverseLabels", "UniverseMember", "UniverseRecord", "WalkCounters",
        ]);
    }

    [Fact]
    public void The_domain_has_no_namespace_beyond_the_known_ones_so_the_simulator_adds_nothing_elsewhere()
    {
        Domain.GetExportedTypes().Select(t => t.Namespace ?? string.Empty).Distinct().Except(Namespaces).ShouldBeEmpty();
    }

    [Fact]
    public void The_research_types_do_no_io_read_no_clock_and_draw_no_random_number()
    {
        // spec P3-04, criterion 15: the run record, the hash and the assembly are values and pure functions (the SHA-256 is System.Security.Cryptography, which is not I/O)
        var result = NetArchTest.Rules.Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Research")
            .ShouldNot().HaveDependencyOnAny("System.IO", "System.Net", "System.Data", "System.Threading.Tasks", "System.Diagnostics", "System.TimeProvider", "System.Environment", "System.Random", "System.Text.Json")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue("Violations: " + string.Join(", ", result.FailingTypeNames ?? []));
        NetArchTest.Rules.Types.InAssembly(Domain).That().ResideInNamespace("XDrishti.Domain.Research").ShouldNot().HaveDependencyOnAny("System.Security.Cryptography").GetResult().IsSuccessful.ShouldBeFalse("the scan sees the hash");
    }

    [Fact]
    public void The_research_types_are_plain_values_that_depend_on_no_other_project()
    {
        var research = Domain.GetExportedTypes().Where(t => t.Namespace == "XDrishti.Domain.Research").ToList();

        research.ShouldAllBe(t => t.IsEnum || t.IsSealed);
        Domain.GetReferencedAssemblies().Select(a => a.Name).ShouldNotContain(n => n != null && n.StartsWith("XDrishti.", StringComparison.Ordinal));
    }
}
