using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>The tracked shares with their closes and the ex-dates of their corporate actions (P3-01e).</summary>
public sealed record BreadthData(IReadOnlyList<BreadthInput> Shares, IReadOnlyDictionary<Guid, IReadOnlySet<DateOnly>> ExDates);

/// <summary>Reads what the breadth of the owner's tracked shares is computed from. Read only.</summary>
public interface IBreadthSource
{
    /// <summary>
    /// The tracked, not retired instruments of type <c>share</c> (ETFs excluded: an index ETF would vote with the index), each with the closes of its complete <c>1D</c> rows from <paramref name="from"/> on (one read per share, raw, never the
    /// adjusted column), and the ex-dates of the corporate actions of those shares whatever their status.
    /// </summary>
    ValueTask<BreadthData> ReadAsync(DateOnly from, CancellationToken cancellationToken);
}
