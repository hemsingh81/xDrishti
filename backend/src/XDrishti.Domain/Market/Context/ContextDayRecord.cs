namespace XDrishti.Domain.Market.Context;

/// <summary>
/// One stored day of the market context (<c>market.context_daily</c>): the regime index high, low and close and the volatility index close when the day has one, with the number of 1-minute bars each came from. The checks of the
/// table are the validation of the indicator library, so a record that passes <see cref="ContextDerivation"/> can never be refused by it. <c>Breadth</c> (percent, 0 to 100) and <c>BreadthUniverse</c> are those of the stored breadth when its state is <c>value</c> (P3-01e), else null.
/// </summary>
public sealed record ContextDayRecord(DateOnly Date, double IndexHigh, double IndexLow, double IndexClose, double? VixClose, int IndexBars, int? VixBars, double? Breadth = null, int? BreadthUniverse = null);
