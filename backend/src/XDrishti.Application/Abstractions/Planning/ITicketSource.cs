using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>One conditional ticket with the cell it comes from.</summary>
/// <param name="Draft">The conditional ticket, built from data up to the close of the session only.</param>
/// <param name="Cell">The cell.</param>
/// <param name="Stats">The cell statistics as of a date before the plan date; null when none.</param>
/// <param name="State">The cell state before the plan date; null when not known.</param>
public sealed record TicketCandidate(TicketDraft Draft, CellId Cell, CellStatsRow? Stats, CellState? State);

/// <summary>What the source found.</summary>
/// <param name="Candidates">The conditional tickets.</param>
/// <param name="Rejected">How many (instrument, cell) pairs had a part false at the close.</param>
/// <param name="Unavailable">How many could not be classified from closed data.</param>
/// <param name="UnavailableReasons">Distinct reasons, sorted, capped.</param>
public sealed record TicketSourceResult(IReadOnlyList<TicketCandidate> Candidates, int Rejected, int Unavailable, IReadOnlyList<string> UnavailableReasons);

/// <summary>Where the conditional tickets of the next session come from (P7-04): the stored cells, the stored strategies and the bars up to the close of the session. Read only; nothing here may place an order.</summary>
public interface ITicketSource
{
    /// <summary>Builds the tickets for <paramref name="forDate"/> from data up to the close of <paramref name="session"/>.</summary>
    ValueTask<Result<TicketSourceResult>> ReadAsync(DateOnly session, DateOnly forDate, CancellationToken cancellationToken);
}
