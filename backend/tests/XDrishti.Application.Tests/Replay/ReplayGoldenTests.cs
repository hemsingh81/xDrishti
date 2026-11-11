#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Planning;
using XDrishti.Application.Replay;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Tests.Replay;

/// <summary>
/// SR-01 AC 5: for a session where the live builder stores a plan, a replay with the start versions, frozen at that session, the same book and the same settings gives the same taken items and the same block reasons. The live side here is the live
/// <see cref="SelectionPlanBuilder"/> over a plan context written by hand from the invented market (not read through the replay facts), so a difference in how the replay reads the facts, sizes the account or maps a reason is a failure, not a tolerance.
/// </summary>
public sealed class ReplayGoldenTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;


    private sealed class HandWrittenContext(ReplayWorld world, IReadOnlyList<ReplayTrade> open) : IPlanContextSource
    {
        public ValueTask<Result<PlanContext>> ReadAsync(PlanContextRequest request, CancellationToken cancellationToken)
        {
            var p = ReplayRiskProfile.Standard;
            double Risk(ReplayTrade t) => (double)(t.Qty * (t.EntryPrice - t.StopPrice)) / 1_000_000 * 100;
            string Sector(string symbol) => symbol == "DDD" ? "Sector2" : "Sector1";
            var book = open.Select(t => new BookPosition(t.Item.Symbol.ToUpperInvariant(), "BUY", "swing", Risk(t), Sector(t.Item.Symbol), ["B"])).ToList();
            var openRisk = open.Sum(Risk);
            var notional = (double)open.Sum(t => t.Qty * t.EntryPrice);
            var account = new ContextAccount(ReplayBook.AccountId, TradingMode.Paper, 1_000_000, p.RiskPerTradePct, p.MaxCapitalPerTradePct, p.MaxTotalRiskPct, openRisk, 1_000_000 - notional, p.MaxPerSector, p.DailyLossPct, 0, open.Select(t => t.Item.Symbol.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal));
            var instruments = request.Instruments.Concat(open.Select(t => new ContextInstrument(t.InstrumentId, t.Item.Symbol))).DistinctBy(i => i.Symbol)
                .ToDictionary(i => i.Symbol.ToUpperInvariant(), i => new InstrumentFacts(Sector(i.Symbol), ["B"], false), StringComparer.Ordinal);
            var returns = new Dictionary<string, IReadOnlyList<DailyReturn>>(StringComparer.Ordinal);
            foreach (var i in request.Instruments)
            {
                var closes = world.Close[i.Symbol].Where(c => c.Key <= request.Session && c.Key >= request.Session.AddDays(-ReplayFacts.ReturnLookbackDays)).Select(c => (c.Key, c.Value));
                returns[i.Symbol] = ReturnSeries.FromCloses(closes, request.Session);
            }

            var table = request.Instruments.Count < 2 ? null : CorrelationTable.FromReturns(returns, request.ForDate, [.. request.Instruments.Select(i => i.Symbol)]);
            var day = world.ContextVix.Keys.Where(k => k < request.ForDate).Select(k => (DateOnly?)k).Max();
            MarketGateInput? gate = day is { } d ? new MarketGateInput(d, world.ContextVix[d], 0.6, null) : null;
            return ValueTask.FromResult(Result.Success(new PlanContext(book, instruments, [account], table, gate, [], ["live"], BookFromLedger: true, MarketNote: gate is null ? null : XDrishti.Application.Abstractions.Planning.PlanContextNotes.MarketBreadth(100))));
        }
    }

    /// <summary>Replays the sessions, then builds the live plan of the session at <paramref name="index"/> from the hand-written context and compares the taken items (with quantities) and the block reasons. Returns the blocked reasons for the caller's own checks.</summary>
    private static async Task<(IReadOnlyList<ReplayBlocked> Blocked, int Taken, int OpenAtSession)> Golden(ReplayWorld world, int sessions, int index)
    {
        var rig = ReplayRig.Create(world);
        var p = ReplayRig.Params(sessions);
        var run = await rig.Driver.RunAsync(p, Ct);
        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Error.Message : string.Empty);
        var record = run.Value.Sessions[index];
        var session = record.Session;
        var forDate = run.Value.Sessions[index + 1].Session;

        var found = await rig.Candidates.ReadAsync(p, session, forDate, Ct);
        var open = run.Value.Book.OpenAt(session);
        var sizing = Substitute.For<ISizingInputs>();
        sizing.ReadAsync(session, forDate, Arg.Any<IReadOnlyList<ContextInstrument>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new SizingInputs(XDrishti.Domain.Selection.SizeScaling.SizingSettings.Defaults, 0, new Dictionary<string, SymbolSizingInputs>())));
        var baskets = Substitute.For<IBasketSettingsStore>();
        baskets.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new BasketSettingsState(world.BasketSettings, 1)));
        var builder = new SelectionPlanBuilder(
            new FixedTicketSource(new TicketSourceResult(found.Found, found.Rejected, found.Unavailable, [])), new HandWrittenContext(world, open), NullLogger<SelectionPlanBuilder>.Instance, ReplayWorld.Loose, world.Smart ?? new SmartRulesOptions(), world.Fill, null, null, sizing, baskets);
        var close = new DateTimeOffset(session.ToDateTime(new TimeOnly(15, 30)), TimeSpan.FromHours(5.5));
        var plan = (await builder.BuildAsync(new PlanBuildRequest(session, forDate, close, close), Ct)).Value;

        var liveTaken = plan.Items.Where(i => i.Group == TicketGroup.Primary).Select(i => (i.Symbol, Qty: i.Allocations.Single().Quantity)).OrderBy(t => t.Symbol, StringComparer.Ordinal).ToList();
        var liveBlocked = plan.Items.Where(i => i.Group != TicketGroup.Primary).Select(BlockReasonMap.Of).OrderBy(b => b.Symbol, StringComparer.Ordinal).ToList();
        var replayTaken = record.Fills.Where(f => f.Status == ReplayFill.Filled).Select(f => (f.Symbol, f.Qty)).OrderBy(t => t.Symbol, StringComparer.Ordinal).ToList();

        replayTaken.ShouldBe(liveTaken);
        record.Taken.Select(t => t.Symbol).Order(StringComparer.Ordinal).ShouldBe(liveTaken.Select(t => t.Symbol));
        record.Blocked.OrderBy(b => b.Symbol, StringComparer.Ordinal).ShouldBe(liveBlocked);
        return (liveBlocked, liveTaken.Count, open.Count);
    }

    private static ReplayWorld Four(params double[] ks)
    {
        var symbols = new[] { "AAA", "BBB", "CCC", "DDD", "EEE" }[..ks.Length];
        var world = new ReplayWorld().WithDefaults(symbols);
        for (var i = 0; i < symbols.Length; i++)
        {
            world.Rising(symbols[i], ks[i]);
        }

        return world;
    }

    [Fact]
    public async Task A_position_limit_block_matches_the_live_plan_with_an_empty_book()
    {
        var world = Four(0.7, 2.0, 3.3, 4.9);
        world.Fill = new FillConstraints(MaxEntriesPerDay: 3, MaxOpenPositions: 2, MaxPerSector: 10, MaxPairCorrelation: 1.0, MaxIntraday: 10, MaxSwing: 10, MaxNetLongRiskPct: 50, MaxNetShortRiskPct: 50, MaxPerTradeRiskPct: 2.0);

        var (blocked, taken, open) = await Golden(world, 2, 0);

        (taken, open).ShouldBe((2, 0), "the fixture takes two and blocks two: a golden test of nothing proves nothing");
        blocked.Count.ShouldBe(2);
        blocked.ShouldAllBe(b => b.Reason == BlockReason.MaxPositions);
    }

    [Fact]
    public async Task A_session_with_an_open_book_a_reduced_gate_and_smart_rule_blocks_matches_the_live_plan()
    {
        var world = Four(0.7, 2.0, 3.3, 4.9, 5.7);
        world.Fill = new FillConstraints(MaxEntriesPerDay: 5, MaxOpenPositions: 6, MaxPerSector: 10, MaxPairCorrelation: 1.0, MaxIntraday: 10, MaxSwing: 10, MaxNetLongRiskPct: 50, MaxNetShortRiskPct: 50, MaxPerTradeRiskPct: 2.0);
        world.Smart = new SmartRulesOptions(CorrelationEnabled: false);
        world.ContextVix[ReplayWorld.Start.AddDays(-3)] = 19;
        world.ContextVix[ReplayWorld.Start] = 19; // India VIX at 19: the gate is reduced (18 reduces, 22 cancels), the plan keeps half and cuts the risk to 0.75

        var (blocked, _, open) = await Golden(world, 3, 1);

        open.ShouldBeGreaterThan(0, "the book is not empty");
        blocked.ShouldContain(b => b.Reason == BlockReason.Gate);
        blocked.ShouldContain(b => b.Reason == BlockReason.SmartRule); // the symbols already held come back as candidates
    }

    [Fact]
    public async Task A_same_bet_set_aside_by_the_smart_rules_matches_the_live_plan()
    {
        var world = Four(0.7, 0.7, 3.3, 4.9); // AAA and BBB move alike: one bet
        world.Fill = new FillConstraints(MaxPairCorrelation: 1.0, MaxPerSector: 10, MaxIntraday: 10, MaxSwing: 10, MaxNetLongRiskPct: 50, MaxNetShortRiskPct: 50, MaxPerTradeRiskPct: 2.0);

        var (blocked, _, _) = await Golden(world, 2, 0);

        blocked.ShouldContain(b => b.Reason == BlockReason.SmartRule && b.Detail.StartsWith("same_bet", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_cluster_cap_block_matches_the_live_plan()
    {
        var world = Four(0.7, 0.7, 3.3, 4.9);
        world.Fill = new FillConstraints(MaxPairCorrelation: 1.0, MaxPerSector: 10, MaxIntraday: 10, MaxSwing: 10, MaxNetLongRiskPct: 50, MaxNetShortRiskPct: 50, MaxPerTradeRiskPct: 2.0);
        world.Smart = new SmartRulesOptions(CorrelationEnabled: false);
        world.BasketSettings = new BasketSettings("tracked", 0.6, 0.7);

        var (blocked, _, _) = await Golden(world, 2, 0);

        blocked.ShouldContain(b => b.Reason == BlockReason.ClusterCap);
    }
}
