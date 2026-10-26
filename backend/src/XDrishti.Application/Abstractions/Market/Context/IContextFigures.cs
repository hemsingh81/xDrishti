using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>
/// The market context as of a date (P3-01d, ADR 0033), for the plan, the market gate and the regime breakdowns. The figures are those of the latest stored day strictly before the date; a value that is not ready is null; before the first day, or past the age
/// bound, the answer is unavailable with the reason. Application names no indicator type: the adapter in Infrastructure builds and caches the library's context.
/// </summary>
public interface IContextFigures
{
    ValueTask<ContextRead> AsOfAsync(DateOnly date, CancellationToken cancellationToken);
}
