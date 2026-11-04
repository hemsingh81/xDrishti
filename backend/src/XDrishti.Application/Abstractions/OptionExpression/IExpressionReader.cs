using XDrishti.Domain.Options.Evaluation;

namespace XDrishti.Application.Abstractions.OptionExpression;

/// <summary>An expression run found for a cash run and a profile: its id and the time it was stored. Counts are not here: the run row counts the whole run, hold-out included. <see cref="From"/> and <see cref="To"/> are the dates of the condor population typed on the command line (null for a re-mark of a cash run, whose dates are the cash run's).</summary>
public sealed record ExpressionRunRef(string RunId, DateTimeOffset CreatedAt, DateOnly? From = null, DateOnly? To = null);

/// <summary>The dates of an expression run, no figure: the first and last entry of each side's closed trades, and the last date of a paired trade.</summary>
public sealed record ExpressionDates(IReadOnlyList<SideBounds> Sides, DateOnly? LastPaired);

/// <summary>One paired trade as stored: the option and cash <c>R_equiv</c>, whether the option figure is net, and the labels the flags are read from.</summary>
public sealed record ExpressionPairRow(string TradeKey, DateTimeOffset EntryTs, DateTimeOffset ExitTs, double OptionR, double CashR, bool Net, string ExpirySource, string Sizable, IReadOnlyList<string> Labels);

/// <summary>
/// The reads of protocol O over a stored expression run (P14-09), select only. Every read that returns figures takes the windows to read: the filter is in the query, so a caller that asks for the development windows is never given a hold-out trade.
/// A window is a side and an inclusive range of India entry dates; at most <see cref="MaxWindows"/> are read at once.
/// </summary>
public interface IExpressionReader
{
    /// <summary>The most windows one read takes.</summary>
    public const int MaxWindows = 500;

    /// <summary>The latest run stored for the cash run and the profile (by time, then id), or null.</summary>
    ValueTask<ExpressionRunRef?> FindRunAsync(string cashRunKey, string profileHash, CancellationToken cancellationToken);

    /// <summary>The dates of the run.</summary>
    ValueTask<ExpressionDates> DatesAsync(string runId, CancellationToken cancellationToken);

    /// <summary>The paired trades entered inside the windows, oldest first.</summary>
    ValueTask<IReadOnlyList<ExpressionPairRow>> PairedAsync(string runId, IReadOnlyList<ExpressionWindow> windows, CancellationToken cancellationToken);

    /// <summary>The counts by fate of the trades entered inside the windows that are neither paired nor open at the end of the data.</summary>
    ValueTask<IReadOnlyDictionary<string, int>> UnpairedAsync(string runId, IReadOnlyList<ExpressionWindow> windows, CancellationToken cancellationToken);
}
