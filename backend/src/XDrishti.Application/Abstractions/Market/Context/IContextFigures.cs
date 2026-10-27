using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>
/// The market context as of a date (P3-01d, ADR 0033), for the plan, the market gate and the regime breakdowns. The figures are those of the latest stored day strictly before the date; a value that is not ready is null; before the first day, or past the age
/// bound, the answer is unavailable with the reason. Application names no indicator type: the adapter in Infrastructure builds and caches the library's context.
/// </summary>
public interface IContextFigures
{
    ValueTask<ContextRead> AsOfAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>
    /// The answers of <see cref="AsOfAsync"/> for many dates from one read of the stored days (P6-02): the same answer for each date as asking one at a time, with the stored days checked once. Duplicate dates give one entry.
    /// </summary>
    ValueTask<IReadOnlyDictionary<DateOnly, ContextRead>> AsOfManyAsync(IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken);
}
