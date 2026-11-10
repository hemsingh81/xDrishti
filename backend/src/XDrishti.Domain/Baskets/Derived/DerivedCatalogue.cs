namespace XDrishti.Domain.Baskets.Derived;

/// <summary>The fourteen derived baskets as data (spec BK-02). Defaults of the numbers live in <see cref="DerivedParams"/>.</summary>
public static class DerivedCatalogue
{
    public static IReadOnlyList<DerivedDefinition> All { get; } =
    [
        new(1, "Most volatile", "The 50 instruments with the highest Wilder ATR(14) as a share of the close.", "Not an index; not a forecast of tomorrow's range.", 20, 60, DerivedCadence.Daily, SelectionForm.TopK),
        new(2, "Most moving", "The 30 instruments with the largest mean absolute daily return over 5 sessions.", "Not the biggest gainers; direction is ignored.", 5, 25, DerivedCadence.Daily, SelectionForm.TopK),
        new(3, "Most liquid", "The 100 instruments with the highest median traded value (close x volume) over 20 sessions.", "Not a market-cap ranking; a block-deal day does not move the median.", 20, 60, DerivedCadence.Daily, SelectionForm.TopK),
        new(4, "Gappers", "Instruments opening beyond max(2 %, 1.5 x ATR%) from the previous close on strong first-5-minute volume, formed at 09:20 IST and frozen.", "Not a prediction that the gap holds or fills.", 14, 40, DerivedCadence.FrozenIntraday, SelectionForm.Threshold, NeedsIntraday: true),
        new(5, "High beta", "The 50 instruments with the highest beta above 1 to the Nifty 50 over 250 daily returns.", "Not NSE's Nifty High Beta 50; our own rule.", 250, 200, DerivedCadence.Weekly, SelectionForm.TopK),
        new(6, "Momentum 12-1", "The 30 instruments with the best return from 12 months ago to 1 month ago.", "Not NSE's Momentum 30; our own 12-1 rule.", 252, 252, DerivedCadence.Monthly, SelectionForm.TopK),
        new(7, "Mean-reverting", "The 50 instruments with the lowest variance ratio, passing variance ratio, autocorrelation and efficiency-ratio legs together.", "Not a promise of reversion; the statistics are noisy at one year.", 250, 200, DerivedCadence.Weekly, SelectionForm.TopK, Ascending: true),
        new(8, "Trending", "The 50 instruments with the highest efficiency ratio among those with ER(20) above 0.5 and ADX(14) above 25.", "Not a buy list; a trend can end tomorrow.", 60, 60, DerivedCadence.Daily, SelectionForm.TopK),
        new(9, "Near 52-week high", "Instruments within 3 % of their 252-day high, liquid, and above their 200-day average.", "Not NSE's 52-week-high index; our own rule.", 252, 252, DerivedCadence.Daily, SelectionForm.Threshold),
        new(10, "Low volatility", "The 30 lowest realised-volatility instruments among the 200 most liquid.", "Not NSE's Low Volatility 30; our own rule.", 252, 252, DerivedCadence.Monthly, SelectionForm.TopK, Ascending: true),
        new(11, "High relative volume", "Instruments whose last volume is at least twice the 20-session median and priced at least Rs 50.", "Not news-driven; volume can spike for dull reasons.", 20, 40, DerivedCadence.Daily, SelectionForm.Threshold),
        new(12, "Opening-range wide", "The 30 instruments with an opening range (09:15-09:30) of at least 1.5 x their 20-day median and 0.8 %, frozen at 09:30 IST.", "Not a breakout signal.", 20, 15, DerivedCadence.FrozenIntraday, SelectionForm.TopK, NeedsIntraday: true),
        new(13, "Intraday-tradable (proxy)", "Instruments liquid enough, with few empty minutes and a minute range of at least 3 ticks, not ASM/GSM, not in the ban list, series EQ.", "Not a bid/ask test: no depth is stored, so this is a proxy.", 20, 15, DerivedCadence.Daily, SelectionForm.Threshold),
        new(14, "Return-correlation clusters", "Groups of instruments whose market-neutral daily returns move together (Spearman, average linkage), named by majority industry.", "Not sectors; clusters change between windows (see the stability score).", 120, 100, DerivedCadence.Monthly, SelectionForm.Clusters),
    ];

    public static DerivedDefinition Get(int id) => All.Single(d => d.Id == id);

    public static IDerivedEvaluator Evaluator(int id) => new DerivedEvaluator(Get(id));
}
