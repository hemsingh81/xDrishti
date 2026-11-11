using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay;
using XDrishti.Domain.Replay;
using XDrishti.Infrastructure.Planning;
using XDrishti.Simulation.Scanning;

namespace XDrishti.Infrastructure.Replay;

/// <summary>
/// The conditional tickets of a replayed session (SR-01), built the way the live <c>StoredTicketSource</c> builds them but from <see cref="IReplayFacts"/> only: the members, the cells (statistics, state, strategy version under the rule) and the 1-minute bars all as of the close
/// of the session, with the pure <see cref="TicketBuilder"/> over them. A cell whose strategy has no version under the rule is listed, not guessed. A member whose bars do not reach the last minute of the session counts as incomplete. Read only. One instance serves one run
/// (it keeps the prepared strategies).
/// </summary>
internal sealed class ReplayCandidateSource(IReplayFacts facts) : IReplayCandidates
{
    private readonly Dictionary<string, (PreparedTicketStrategy? Strategy, string? Problem)> _prepared = new(StringComparer.Ordinal);

    public async ValueTask<ReplayCandidateSet> ReadAsync(ReplayParams parameters, DateOnly session, DateOnly forDate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var universe = await facts.UniverseAsync(parameters, session, cancellationToken);
        var cells = await facts.CellsAsync(parameters, session, cancellationToken);
        var calendar = await facts.CalendarAsync(cancellationToken);
        var usable = new List<ClassifiedCell>();
        var noVersion = new List<string>();
        var tally = new ClassificationTally();
        foreach (var cell in cells.Cells)
        {
            var id = cell.Stored.Row.Cell;
            if (cell.Version is not { } version)
            {
                noVersion.Add(id.Key);
                continue;
            }

            var key = id.Key + "|" + version.Hash;
            if (!_prepared.TryGetValue(key, out var prepared))
            {
                _prepared[key] = prepared = TicketClassification.Prepare(id, version);
            }

            if (prepared.Strategy is null)
            {
                tally.Unavailable++;
                continue;
            }

            usable.Add(new ClassifiedCell(cell.Stored, cell.State, prepared.Strategy));
        }

        var found = new List<TicketCandidate>();
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

            TicketClassification.Classify(usable, member.Id, member.Symbol, bars, session, forDate, calendar, found, tally, cancellationToken);
        }

        return new ReplayCandidateSet(found, tally.Rejected, tally.Unavailable, universe.Members.Count, incomplete, noVersion, universe.Survivorship, cells.Notes);
    }
}
