#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Planning;
using XDrishti.Application.Replay;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Replay;

/// <summary>
/// The engine of the invented market: a swing trade decided at the close of the session <c>from</c> enters at the close of the next session and leaves when a close reaches 4 % above (target) or 2 % below (stop) the entry; it reads no close after <c>to</c> and
/// records the latest date it was asked for, so a test can see that nothing past the allowed date was read.
/// </summary>
internal sealed class FakeSimulation(ReplayWorld world) : IQueryHandler<RunSimulationQuery, Result<SimulationRun>>
{
    public DateOnly MaxDateRead { get; private set; }

    public ValueTask<Result<SimulationRun>> HandleAsync(RunSimulationQuery query, CancellationToken cancellationToken)
    {
        var request = query.Request;
        var id = request.InstrumentIds![0];
        var symbol = ReplayWorld.Ids.First(p => p.Value == id).Key;
        var (from, to) = (request.From, request.To);
        MaxDateRead = to > MaxDateRead ? to : MaxDateRead;
        var closes = world.Close[symbol];
        var sessions = closes.Keys.Where(d => d <= to).ToList();
        var trades = new List<SimTrade>();
        var open = new List<SimTrade>();
        var entryDay = sessions.FirstOrDefault(d => d > from);
        if (entryDay != default)
        {
            var entry = closes[entryDay];
            var (stop, target) = (entry * 0.98, entry * 1.04);
            DateOnly? exitDay = null;
            var exitPrice = 0.0;
            var reason = ExitReason.EndOfData;
            foreach (var d in sessions.Where(d => d > entryDay))
            {
                if (closes[d] <= stop || closes[d] >= target)
                {
                    exitDay = d;
                    reason = closes[d] >= target ? ExitReason.Target : ExitReason.Stop;
                    exitPrice = closes[d];
                    break;
                }
            }

            var rr = exitDay is null ? 0 : (exitPrice - entry) / (entry - stop);
            var trade = FabricatedRun.Trade(id, symbol, Side.Buy, entryDay, reason, 555, entry, stop, target, exitPrice, rr, rr, strategy: "S1") with
            {
                DecisionEpochSeconds = Day.Epoch(from, 930),
                TriggerTimeframe = Timeframe.D1,
                EntryEpochSeconds = Day.Epoch(entryDay, 555),
                ExitEpochSeconds = Day.Epoch(exitDay ?? entryDay, 600),
                EntryFilledPrice = entry,
                ExitFilledPrice = exitPrice,
            };
            (exitDay is null ? open : trades).Add(trade);
        }

        var run = FabricatedRun.Assemble(FabricatedRun.List((id, symbol)), [FabricatedRun.Ran(id, symbol, from.AddDays(-30), to)], trades, open, from: from, to: to, strategy: "S1");
        return ValueTask.FromResult(Result.Success(run));
    }
}

/// <summary>The candidates of the invented market: a BUY ticket for every member whose last close, up to the session, is above the one before (read through the clamped facts), with the planned entry at that close and the stop 2 % below.</summary>
internal sealed class BarCandidates(IReplayFacts facts) : IReplayCandidates
{
    public async ValueTask<ReplayCandidateSet> ReadAsync(ReplayParams parameters, DateOnly session, DateOnly forDate, CancellationToken cancellationToken)
    {
        var universe = await facts.UniverseAsync(parameters, session, cancellationToken);
        var cells = await facts.CellsAsync(parameters, session, cancellationToken);
        var found = new List<TicketCandidate>();
        var incomplete = 0;
        var rejected = 0;
        foreach (var member in universe.Members)
        {
            var bars = await facts.MinuteBarsAsync(member.Id, session, cancellationToken);
            if (bars.Count < 2 || SessionClock.DateOf(bars[^1].EpochSeconds) != session)
            {
                incomplete++;
                continue;
            }

            foreach (var cell in cells.Cells.Where(c => c.Version is not null))
            {
                if (bars[^1].Close <= bars[^2].Close)
                {
                    rejected++;
                    continue;
                }

                var entry = bars[^1].Close;
                var draft = new TicketDraft(
                    TicketVerdict.Conditional, member.Symbol, "BUY", cell.Stored.Row.Cell.Strategy, cell.Version!.Hash, session, SetupTruth.True, SetupTruth.Unknown, SetupTruth.False, SetupTruth.Unknown, [],
                    "reason", "watch the breakout", "stop below the low", "target 2 x risk", "valid for the day", entry * 0.98, entry, 2.0, 50_000_000, ["setup"]);
                found.Add(new TicketCandidate(draft, cell.Stored.Row.Cell, cell.Stored.Row, cell.State, member.Id));
            }
        }

        return new ReplayCandidateSet(found, rejected, 0, universe.Members.Count, incomplete, [.. cells.Cells.Where(c => c.Version is null).Select(c => c.Stored.Row.Cell.Key)], universe.Survivorship, cells.Notes);
    }
}

/// <summary>The pieces of a replay over a <see cref="ReplayWorld"/>: the real facts, the real session runner and driver, the engine of the invented market.</summary>
internal sealed class ReplayRig
{
    private ReplayRig(ReplayWorld world, double incompleteShare, Func<IReplayFacts, IReplayCandidates>? candidates)
    {
        World = world;
        Facts = world.Facts(incompleteShare);
        Simulation = new FakeSimulation(world);
        Candidates = candidates?.Invoke(Facts) ?? new BarCandidates(Facts);
        Runner = new ReplaySessionRunner(Facts, Candidates, new TicketReplayer(world.Strategies!, world.Baskets!, Simulation));
        Driver = new ReplayDriver(Facts, Runner);
    }

    public ReplayWorld World { get; }

    public ReplayFacts Facts { get; }

    public FakeSimulation Simulation { get; }

    public IReplayCandidates Candidates { get; }

    public ReplaySessionRunner Runner { get; }

    public ReplayDriver Driver { get; }

    public static ReplayRig Create(ReplayWorld world, double incompleteShare = 0.2, Func<IReplayFacts, IReplayCandidates>? candidates = null) => new(world, incompleteShare, candidates);

    public static ReplayParams Params(int sessions = 5, string? sheet = null, decimal capital = 1_000_000m, ReplayMode mode = ReplayMode.Frozen, ReplayVersionRule rule = ReplayVersionRule.Start) =>
        new(ReplayWorld.Start, ReplayWorld.RunSessions[sessions - 1], new ReplayScope([ReplayWorld.Basket], []), capital, ReplayRiskProfile.Standard, sheet, rule, mode);
}
