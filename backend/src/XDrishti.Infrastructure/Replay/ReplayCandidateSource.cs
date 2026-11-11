using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Replay;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Replay;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;

namespace XDrishti.Infrastructure.Replay;

/// <summary>
/// The conditional tickets of a replayed session (SR-01), built the way the live <c>StoredTicketSource</c> builds them but from <see cref="IReplayFacts"/> only: the members, the cells (statistics, state, strategy version under the rule) and the 1-minute bars all as of the close
/// of the session, with the pure <see cref="TicketBuilder"/> over them. A cell whose strategy has no version under the rule is listed, not guessed. A member whose bars do not reach the last minute of the session counts as incomplete. Read only. One instance serves one run
/// (it keeps the prepared strategies).
/// </summary>
internal sealed class ReplayCandidateSource(IReplayFacts facts) : IReplayCandidates
{
    private readonly Dictionary<string, PreparedTicketStrategy> _prepared = new(StringComparer.Ordinal);

    public async ValueTask<ReplayCandidateSet> ReadAsync(ReplayParams parameters, DateOnly session, DateOnly forDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var universe = await facts.UniverseAsync(parameters, session, cancellationToken);
        var cells = await facts.CellsAsync(parameters, session, cancellationToken);
        var calendar = await facts.CalendarAsync(cancellationToken);
        var usable = new List<(ReplayCell Cell, PreparedTicketStrategy Strategy)>();
        var noVersion = new List<string>();
        var unavailable = 0;
        foreach (var cell in cells.Cells)
        {
            var id = cell.Stored.Row.Cell;
            if (cell.Version is not { } version)
            {
                noVersion.Add(id.Key);
                continue;
            }

            if (!Timeframes.TryParse(id.Timeframe, out var timeframe) || !TrySide(id.Side, out var side))
            {
                unavailable++;
                continue;
            }

            var key = id.Key + "|" + version.Hash;
            if (!_prepared.TryGetValue(key, out var prepared))
            {
                _prepared[key] = prepared = PreparedTicketStrategy.Prepare(version.SourceText, version.Hash, timeframe, side, id.Style, id.Exit);
            }

            if (prepared.Problem is not null)
            {
                unavailable++;
                continue;
            }

            usable.Add((cell, prepared));
        }

        var found = new List<TicketCandidate>();
        var rejected = 0;
        var incomplete = 0;
        foreach (var member in universe.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var minutes = await facts.MinuteBarsAsync(member.Id, session, cancellationToken);
            var bars = TicketBars.Prepare(minutes, session, calendar); // truncated and sorted once for every cell of the instrument
            if (!bars.IsComplete)
            {
                incomplete++;
            }

            foreach (var (cell, strategy) in usable)
            {
                var draft = TicketBuilder.Build(strategy, bars, member.Symbol, session, forDate, calendar);
                switch (draft.Verdict)
                {
                    case TicketVerdict.Conditional:
                        found.Add(new TicketCandidate(draft, cell.Stored.Row.Cell, cell.Stored.Row, cell.State, member.Id));
                        break;
                    case TicketVerdict.Rejected:
                        rejected++;
                        break;
                    default:
                        unavailable++;
                        break;
                }
            }
        }

        return new ReplayCandidateSet(found, rejected, unavailable, universe.Members.Count, incomplete, noVersion, universe.Survivorship, cells.Notes);
    }

    private static bool TrySide(string text, out Side side)
    {
        side = text == "SELL" ? Side.Sell : Side.Buy;
        return text is "BUY" or "SELL";
    }
}
