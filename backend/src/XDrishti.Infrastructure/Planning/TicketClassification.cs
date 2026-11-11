using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;

namespace XDrishti.Infrastructure.Planning;

/// <summary>A cell ready to classify instruments: its statistics row, its state and its compiled strategy.</summary>
internal sealed record ClassifiedCell(StoredCellStats Stored, CellState? State, PreparedTicketStrategy Strategy);

/// <summary>What classifying found beyond the tickets: pairs with a part false, pairs that could not be told, and the distinct generic reasons.</summary>
internal sealed class ClassificationTally
{
    private const int MaxReasons = 20;

    public int Rejected { get; set; }

    public int Unavailable { get; set; }

    public SortedSet<string> Reasons { get; } = new(StringComparer.Ordinal);

    public void AddReason(string reason)
    {
        if (Reasons.Count < MaxReasons)
        {
            Reasons.Add(reason);
        }
    }
}

/// <summary>
/// The one place that turns a stored cell and its strategy version into a compiled cell strategy, and a prepared instrument into tickets (spec P7-04). The live <see cref="StoredTicketSource"/> and the system replay's candidate source (SR-01) both call it,
/// so the candidate side of a replayed session is the live one by construction. Pure; no I/O.
/// </summary>
internal static class TicketClassification
{
    /// <summary>Reads, hash-checks and compiles the strategy of a cell once; null with the reason when the cell or text cannot be used.</summary>
    public static (PreparedTicketStrategy? Strategy, string? Problem) Prepare(CellId cell, StoredVersion version)
    {
        if (!Timeframes.TryParse(cell.Timeframe, out var timeframe) || !TrySide(cell.Side, out var side))
        {
            return (null, "strategy or cell not usable: " + cell.Strategy);
        }

        var prepared = PreparedTicketStrategy.Prepare(version.SourceText, version.Hash, timeframe, side, cell.Style, cell.Exit);
        return prepared.Problem is { } problem ? (null, problem) : (prepared, null);
    }

    /// <summary>Builds the tickets of one instrument for every cell on its prepared bars.</summary>
    public static void Classify(IReadOnlyList<ClassifiedCell> cells, Guid instrumentId, string symbol, TicketBars bars, DateOnly session, DateOnly forDate, TradingCalendar calendar, List<TicketCandidate> found, ClassificationTally tally, CancellationToken cancellationToken)
    {
        foreach (var cell in cells)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var draft = TicketBuilder.Build(cell.Strategy, bars, symbol, session, forDate, calendar);
            switch (draft.Verdict)
            {
                case TicketVerdict.Conditional:
                    found.Add(new TicketCandidate(draft, cell.Stored.Row.Cell, cell.Stored.Row, cell.State, instrumentId));
                    break;
                case TicketVerdict.Rejected:
                    tally.Rejected++;
                    break;
                default:
                    tally.Unavailable++;
                    tally.AddReason(Generic(draft.Reason));
                    break;
            }
        }
    }

    public static bool TrySide(string text, out Side side)
    {
        side = text == "SELL" ? Side.Sell : Side.Buy;
        return text is "BUY" or "SELL";
    }

    /// <summary>The reason without the dates and path lists, so the same cause is one line.</summary>
    private static string Generic(string reason) => reason.Contains("not ready", StringComparison.Ordinal) ? "history not ready at the close"
        : reason.Contains("not complete", StringComparison.Ordinal) ? "session not complete in the bars"
        : reason;
}
