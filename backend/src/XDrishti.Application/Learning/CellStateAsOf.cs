using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The state of a cell from its changelog as it stood before an instant (what a plan for the next day may know). Pure.</summary>
public static class CellStateAsOf
{
    /// <summary>The state from the rows written before <paramref name="before"/>; null when not known.</summary>
    public static CellState? Of(IReadOnlyList<ChangelogEntry> history, DateTimeOffset before)
    {
        ArgumentNullException.ThrowIfNull(history);
        return CellHistory.Replay([.. history.Where(h => h.Entry.At < before)]).State;
    }
}
