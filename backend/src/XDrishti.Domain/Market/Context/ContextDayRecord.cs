namespace XDrishti.Domain.Market.Context;

/// <summary>
/// One stored day of the market context (<c>market.context_daily</c>): the regime index high, low and close and the volatility index close when the day has one, with the number of 1-minute bars each came from. The checks of the
/// table are the validation of the indicator library, so a record that passes <see cref="ContextDerivation"/> can never be refused by it.
/// </summary>
public sealed record ContextDayRecord(DateOnly Date, double IndexHigh, double IndexLow, double IndexClose, double? VixClose, int IndexBars, int? VixBars);
