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
        return new SelectionPlanBuilder(source, NullLogger<SelectionPlanBuilder>.Instance, rules);
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
    public async Task Loosened_rules_rank_the_ticket_primary_with_levels_and_evidence()
    {
        var result = await Builder([Candidate("AAA", "S1"), Candidate("BBB", "S2")], Loose).BuildAsync(Request, CancellationToken.None);

        Assert.All(result.Value.Items, i => Assert.Equal(TicketGroup.Primary, i.Group));
        Assert.Equal([1, 2], result.Value.Items.Select(i => i.Rank));
        var first = result.Value.Items[0];
        Assert.Equal("stop below the low", first.Stop);
        Assert.Equal(40, first.Evidence.Trades);
        Assert.Contains("cell statistics as of 2026-10-22", first.Evidence.DataRange, StringComparison.Ordinal);
        Assert.Contains("smart_rules", first.Evidence.Unavailable);
    }

    [Fact]
    public async Task More_than_the_primary_cut_goes_to_reserve()
    {
        var many = Enumerable.Range(0, SelectionPlanBuilder.MaxPrimary + 2).Select(i => Candidate("S" + i, "STRAT" + i)).ToList();

        var result = await Builder(many, Loose).BuildAsync(Request, CancellationToken.None);

        Assert.Equal(SelectionPlanBuilder.MaxPrimary, result.Value.Items.Count(i => i.Group == TicketGroup.Primary));
        Assert.All(result.Value.Items.Where(i => i.Group == TicketGroup.Reserve), i => Assert.Contains("below_primary_cut", i.Reasons));
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
    public async Task A_source_failure_is_a_failed_result()
    {
        var source = Substitute.For<ITicketSource>();
        source.ReadAsync(Session, ForDate, Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(Result.Failure<TicketSourceResult>(Error.Upstream("x.y", "no"))));

        var result = await new SelectionPlanBuilder(source, NullLogger<SelectionPlanBuilder>.Instance).BuildAsync(Request, CancellationToken.None);

        Assert.True(result.IsFailure);
    }
}
