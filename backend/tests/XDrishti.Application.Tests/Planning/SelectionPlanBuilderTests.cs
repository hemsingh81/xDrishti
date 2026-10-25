#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Tests.Planning;

public sealed class SelectionPlanBuilderTests
{
    private static readonly DateOnly Session = new(2026, 10, 23);
    private static readonly DateOnly ForDate = new(2026, 10, 26);
    private static readonly PlanBuildRequest Request = new(Session, ForDate, NightlyRules.ReviewCutoff(ForDate), new DateTimeOffset(2026, 10, 23, 13, 0, 0, TimeSpan.Zero));

    private static CellStatsRow Stats(string strategy) => new(
        new CellId(strategy, "1D", "BUY", "swing", "fixed"), CellWindow.All, new DateOnly(2026, 10, 22), true, 40, 20, 40, 0.5, 0.5, 0.55, 0.50, 0.6, 2.0, -1.0, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);

    private static TicketCandidate Candidate(string symbol, string strategy, double rr = 2.0)
    {
        var stats = Stats(strategy);
        var draft = new TicketDraft(
            TicketVerdict.Conditional, symbol, "BUY", strategy, new string('a', 64), Session, SetupTruth.True, SetupTruth.Unknown, SetupTruth.False, SetupTruth.Unknown, [],
            "reason", "watch the breakout", "stop below the low", "target 2 x risk", "valid for the day", 98, 100, rr, 50_000_000, ["setup"]);
        return new TicketCandidate(draft, stats.Cell, stats, CellState.Active);
    }

    private static SelectionPlanBuilder Builder(IReadOnlyList<TicketCandidate> found, SelectionRules? rules = null)
    {
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(Session, ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(new TicketSourceResult(found, 1, 2, ["history not ready at the close"]))));
        var context = Substitute.For<IPlanContextSource>();
        context.ReadAsync(Arg.Any<PlanContextRequest>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Success(WiredPlanFixture.Context())));
        return new SelectionPlanBuilder(source, context, NullLogger<SelectionPlanBuilder>.Instance, rules);
    }

    private static readonly SelectionRules Loose = new(OptionalFilters: [FilterCodes.Cost, FilterCodes.Event, FilterCodes.FnoBan, FilterCodes.Surveillance]);

    [Fact]
    public async Task Default_rules_hold_tickets_whose_inputs_are_unavailable_and_list_them_as_rejected_with_reasons()
    {
        var result = await Builder([Candidate("AAA", "S1")]).BuildAsync(Request, CancellationToken.None);

        var item = Assert.Single(result.Value.Items);
        Assert.Equal(TicketGroup.Rejected, item.Group);
        Assert.NotEmpty(item.Reasons);
        Assert.Contains("portfolio_fill", item.Evidence.Unavailable);
        Assert.Empty(item.Allocations);
        Assert.True(result.Value.Provisional);
        Assert.Equal("nightly:2026-10-26:selection", result.Value.PlanKey);
    }

    [Fact]
    public async Task The_same_inputs_give_the_same_plan_and_hash_and_an_empty_source_an_empty_plan()
    {
        var found = new[] { Candidate("AAA", "S1"), Candidate("BBB", "S2") };

        var a = await Builder(found, Loose).BuildAsync(Request, CancellationToken.None);
        var b = await Builder(found.Reverse().ToArray(), Loose).BuildAsync(Request, CancellationToken.None);
        var empty = await Builder([]).BuildAsync(Request, CancellationToken.None);

        Assert.Equal(a.Value.InputHash, b.Value.InputHash);
        Assert.Empty(empty.Value.Items);
    }

    [Fact]
    public async Task A_duplicate_ticket_from_the_source_is_ignored_not_an_exception()
    {
        var a = Candidate("AAA", "S1");

        var result = await Builder([a, a with { }], Loose).BuildAsync(Request, CancellationToken.None);

        Assert.Single(result.Value.Items);
    }

    [Fact]
    public async Task The_plan_carries_its_smart_rule_flags_as_stored_facts_with_the_banner_unclamped()
    {
        var result = await Builder([Candidate("AAA", "S1")], Loose).BuildAsync(Request, CancellationToken.None);

        var flags = result.Value.BasisFlags;
        Assert.NotNull(flags);
        Assert.Equal(1, flags.SchemaVersion);
        Assert.False(string.IsNullOrEmpty(flags.Gate));
        Assert.Equal(PlanBasisFlags.OpenBookAssumedEmpty, flags.OpenBook);
    }

    [Fact]
    public async Task Every_item_says_the_stats_may_belong_to_an_older_strategy_version()
    {
        var result = await Builder([Candidate("AAA", "S1")], Loose).BuildAsync(Request, CancellationToken.None);

        Assert.Contains(SelectionPlanBuilder.StatsVersionCaveat, result.Value.Items[0].Evidence.Unavailable);
        Assert.Equal("stats may belong to an older strategy version", SelectionPlanBuilder.StatsVersionCaveat);
    }

    [Fact]
    public async Task A_source_failure_is_a_failed_result()
    {
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(Session, ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure<TicketSourceResult>(Error.Upstream("x.y", "no"))));

        var result = await new SelectionPlanBuilder(source, Substitute.For<IPlanContextSource>(), NullLogger<SelectionPlanBuilder>.Instance).BuildAsync(Request, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    private static TicketDraft WithLevels(double? stop, StopAnchor? anchor, double? target, string side = "BUY") =>
        Candidate("AAA", "S1").Draft with { Side = side, StopLevel = stop, StopAnchor = anchor, TargetLevel = target, IndicativeEntry = 100, RewardRisk = target is null ? null : 2 };

    [Fact]
    public void A_numeric_stop_and_a_fixed_target_give_all_levels_and_no_reason_code()
    {
        var (levels, code) = SelectionPlanBuilder.LevelsOf(WithLevels(95, StopAnchor.Level, 110));

        Assert.Null(code);
        Assert.Equal(95m, levels!.Stop);
        Assert.Equal(110m, levels.Target);
        Assert.Equal(2m, levels.RewardRisk);
        Assert.Equal(Session, levels.Session);
    }

    [Fact]
    public void A_stop_without_a_number_keeps_only_the_entry_and_says_so()
    {
        var (levels, code) = SelectionPlanBuilder.LevelsOf(WithLevels(null, null, null));

        Assert.Equal(TicketLevels.NotNumericCode, code);
        Assert.Equal(100m, levels!.EntryRef);
        Assert.Null(levels.Stop);
        Assert.Null(levels.Target);
    }

    [Fact]
    public void An_implausible_stop_distance_withholds_stop_and_target()
    {
        var (levels, code) = SelectionPlanBuilder.LevelsOf(WithLevels(60, StopAnchor.Level, 180));

        Assert.Equal(TicketLevels.ImplausibleCode, code);
        Assert.Null(levels!.Stop);
        Assert.Null(levels.Target);
    }

    [Fact]
    public void A_sell_is_mirrored()
    {
        var (levels, code) = SelectionPlanBuilder.LevelsOf(WithLevels(105, StopAnchor.CloseOfD, 90, "SELL"));

        Assert.Null(code);
        Assert.Equal(90m, levels!.Target);
    }

    [Fact]
    public async Task An_item_without_levels_hashes_exactly_as_before_the_slice()
    {
        var without = Candidate("AAA", "S1");
        without = without with { Draft = without.Draft with { IndicativeEntry = null } };

        var plan = await Builder([without], Loose).BuildAsync(Request, CancellationToken.None);

        Assert.Null(plan.Value.Items[0].Levels);
        Assert.Equal("d245739cc3443f27671d1a028abf21d50039d298248cec1433bd8c76693cd7ae", plan.Value.InputHash);
    }

    [Fact]
    public async Task Clamped_text_never_changes_the_stored_numbers()
    {
        var candidate = Candidate("AAA", "S1");
        candidate = candidate with { Draft = candidate.Draft with { StopText = new string('s', 600), StopAnchor = StopAnchor.Level, TargetLevel = 104 } };

        var plan = await Builder([candidate], Loose).BuildAsync(Request, CancellationToken.None);

        var item = plan.Value.Items[0];
        Assert.Equal(500, item.Stop.Length);
        Assert.Equal(98m, item.Levels!.Stop);
        Assert.Equal(104m, item.Levels.Target);
    }

    [Fact]
    public async Task Levels_are_stored_on_the_item_and_join_the_hash_only_when_present()
    {
        var withNumbers = Candidate("AAA", "S1");
        withNumbers = withNumbers with { Draft = withNumbers.Draft with { StopAnchor = StopAnchor.Level, TargetLevel = 104 } };
        var without = Candidate("AAA", "S1");
        without = without with { Draft = without.Draft with { IndicativeEntry = null } };

        var a = await Builder([withNumbers], Loose).BuildAsync(Request, CancellationToken.None);
        var a2 = await Builder([withNumbers], Loose).BuildAsync(Request, CancellationToken.None);
        var b = await Builder([without], Loose).BuildAsync(Request, CancellationToken.None);

        Assert.Equal(98m, a.Value.Items[0].Levels!.Stop);
        Assert.Equal(a.Value.InputHash, a2.Value.InputHash);
        Assert.Null(b.Value.Items[0].Levels);
        Assert.NotEqual(a.Value.InputHash, b.Value.InputHash);
    }
}
