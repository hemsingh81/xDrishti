namespace XDrishti.Domain.Market.Context;

/// <summary>
/// One day of one index as the stored 1-minute bars aggregate to it (the <c>1D</c> row of <c>market.bars_agg</c>): the India <paramref name="Date"/>, its high, low and close, how many 1-minute bars it was built from
/// (<paramref name="Bars"/>) and whether the aggregate holds every minute of the session (<paramref name="Complete"/>).
/// </summary>
public sealed record DailyLeg(DateOnly Date, double High, double Low, double Close, int Bars, bool Complete);
