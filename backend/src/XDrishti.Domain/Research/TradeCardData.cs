using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Research;

/// <summary>
/// What a trade card needs beyond the run record (spec P3-04c, C2): the trade, the data facts of its instrument and the 1-minute bars of the trade day, read again from the stored bars (the record keeps none). <see cref="Bar"/> is documented
/// raw in the Domain, but the bars here are the <b>adjusted</b> ones the replay fed (the same <c>AdjustmentFactors</c> functions), so that the chart and the trade are in the same prices; the card says which it shows.
/// </summary>
/// <param name="Trade">The trade of the run.</param>
/// <param name="Instrument">The record of the instrument the trade came from (adjustments, discontinuities, sessions left out, the tick, the bar-stream hash).</param>
/// <param name="DayBars">The adjusted bars of the trade date from 09:15 to 15:29 India time, oldest first; a missing minute is not in the list.</param>
/// <param name="OpeningBar">The adjusted bar whose minute is 09:15; null when the day has no such bar (a card then says so and prints no other minute under that heading).</param>
/// <param name="Explanation">The conditions at the decision bar, recomputed with the engine (P3-04d); null for a stored run, which has no strategy handle to recompute with.</param>
/// <param name="OpeningBarRaw">The stored 09:15 bar; set only when the adjustment factor of the day is not 1.</param>
public sealed record TradeCardData(RunTrade Trade, InstrumentRun Instrument, IReadOnlyList<Bar> DayBars, Bar? OpeningBar, Bar? OpeningBarRaw, TradeExplanation? Explanation = null);
