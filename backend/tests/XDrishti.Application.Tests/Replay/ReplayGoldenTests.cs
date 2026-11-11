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

    private static readonly DateOnly Session = ReplayWorld.Start;
    private static readonly DateOnly ForDate = new(2026, 10, 6);

    private sealed class HandWrittenContext(ReplayWorld world, IReadOnlyList<string> held) : IPlanContextSource
    {
        public ValueTask<Result<PlanContext>> ReadAsync(PlanContextRequest request, CancellationToken cancellationToken)
        {
            var p = ReplayRiskProfile.Standard;
            var account = new ContextAccount(ReplayBook.AccountId, TradingMode.Paper, 1_000_000, p.RiskPerTradePct, p.MaxCapitalPerTradePct, p.MaxTotalRiskPct, 0, 1_000_000, p.MaxPerSector, p.DailyLossPct, 0, new HashSet<string>(held));
            var instruments = request.Instruments.ToDictionary(i => i.Symbol.ToUpperInvariant(), i => new InstrumentFacts(i.Symbol == "DDD" ? "Sector2" : "Sector1", ["B"], false), StringComparer.Ordinal);
            var returns = new Dictionary<string, IReadOnlyList<DailyReturn>>(StringComparer.Ordinal);
            foreach (var i in request.Instruments)
            {
                var closes = world.Close[i.Symbol].Where(c => c.Key <= request.Session && c.Key >= request.Session.AddDays(-ReplayFacts.ReturnLookbackDays)).Select(c => (c.Key, c.Value));
                returns[i.Symbol] = ReturnSeries.FromCloses(closes, request.Session);
            }

            var table = request.Instruments.Count < 2 ? null : CorrelationTable.FromReturns(returns, request.ForDate, [.. request.Instruments.Select(i => i.Symbol)]);
            return ValueTask.FromResult(Result.Success(new PlanContext([], instruments, [account], table, null, [], ["live"], BookFromLedger: true)));
        }
    }

    [Fact]
    public async Task A_replayed_session_gives_the_taken_items_and_block_reasons_of_the_live_plan_built_from_the_same_facts()
    {
        var world = new ReplayWorld().WithDefaults("AAA", "BBB", "CCC", "DDD");
        world.Rising("AAA", 0.7);
        world.Rising("BBB", 2.0);
        world.Rising("CCC", 3.3);
        world.Rising("DDD", 4.9);
        world.Fill = new FillConstraints(MaxEntriesPerDay: 3, MaxOpenPositions: 2, MaxPerSector: 10, MaxPairCorrelation: 1.0, MaxIntraday: 10, MaxSwing: 10, MaxNetLongRiskPct: 50, MaxNetShortRiskPct: 50, MaxPerTradeRiskPct: 2.0);
        var rig = ReplayRig.Create(world);
        var p = ReplayRig.Params(2); // two sessions so that the fills of the first are inside the range

        // the replay
        var run = await rig.Driver.RunAsync(p, Ct);
        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Error.Message : string.Empty);
        var record = run.Value.Sessions[0];

        // the live plan from the same candidates, a hand-written context, the same rules, limits and settings, an empty book
        var found = await rig.Candidates.ReadAsync(p, Session, ForDate, Ct);
        var sizing = Substitute.For<ISizingInputs>();
        sizing.ReadAsync(Session, ForDate, Arg.Any<IReadOnlyList<ContextInstrument>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new SizingInputs(XDrishti.Domain.Selection.SizeScaling.SizingSettings.Defaults, 0, new Dictionary<string, SymbolSizingInputs>())));
        var baskets = Substitute.For<IBasketSettingsStore>();
        baskets.CurrentAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult(new BasketSettingsState(world.BasketSettings, 1)));
        var builder = new SelectionPlanBuilder(
            new FixedTicketSource(new TicketSourceResult(found.Found, found.Rejected, found.Unavailable, [])), new HandWrittenContext(world, []), NullLogger<SelectionPlanBuilder>.Instance, ReplayWorld.Loose, new SmartRulesOptions(), world.Fill, null, null, sizing, baskets);
        var close = new DateTimeOffset(2026, 10, 5, 15, 30, 0, TimeSpan.FromHours(5.5));
        var plan = (await builder.BuildAsync(new PlanBuildRequest(Session, ForDate, close, close), Ct)).Value;

        var liveTaken = plan.Items.Where(i => i.Group == TicketGroup.Primary).Select(i => (i.Symbol, Qty: i.Allocations.Single().Quantity)).OrderBy(t => t.Symbol, StringComparer.Ordinal).ToList();
        var liveBlocked = plan.Items.Where(i => i.Group != TicketGroup.Primary).Select(BlockReasonMap.Of).OrderBy(b => b.Symbol, StringComparer.Ordinal).ToList();
        var replayTaken = record.Fills.Where(f => f.Status == ReplayFill.Filled).Select(f => (f.Symbol, f.Qty)).OrderBy(t => t.Symbol, StringComparer.Ordinal).ToList();

        liveTaken.Count.ShouldBe(2, "the fixture takes two and blocks two: a golden test of nothing proves nothing");
        liveBlocked.Count.ShouldBe(2);
        replayTaken.ShouldBe(liveTaken);
        record.Taken.Select(t => t.Symbol).Order(StringComparer.Ordinal).ShouldBe(liveTaken.Select(t => t.Symbol));
        record.Blocked.OrderBy(b => b.Symbol, StringComparer.Ordinal).ShouldBe(liveBlocked);
        liveBlocked.ShouldAllBe(b => b.Reason == BlockReason.MaxPositions);
    }
}
