using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>What the pre-open read found for a set of symbols.</summary>
/// <param name="Readings">By symbol; a symbol not listed has nothing available.</param>
/// <param name="Problem">A safe sentence when the read failed as a whole (the data source refused or did not answer), else null.</param>
public sealed record PreOpenSnapshot(IReadOnlyDictionary<string, PreOpenReading> Readings, string? Problem);

/// <summary>
/// The pre-open facts of the approved tickets (spec P7-11): the last price and previous close from the existing read-only market quote, and the average true range from the stored day bars up to the previous close. Read only:
/// nothing behind it may place, modify or cancel an order, and it uses no endpoint or credential beyond the quote read the holdings already use. A failure is a snapshot with a problem, not an exception.
/// </summary>
public interface IPreOpenSource
{
    /// <summary>Reads the facts for the symbols (canonical symbols of the plan's tickets), using only sessions before <paramref name="today"/>.</summary>
    ValueTask<PreOpenSnapshot> ReadAsync(IReadOnlyCollection<string> symbols, DateOnly today, CancellationToken cancellationToken);
}
