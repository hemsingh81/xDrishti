using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Cards;

/// <summary>
/// The chart of a trade card (spec P3-04c, C4): inline SVG built by pure code with a fixed geometry. <c>viewBox="0 0 1000 440"</c>, no <c>xmlns</c> (inline SVG in HTML5 needs none). The plot area is x 70 to 980 and y 20 to 390. A bar at
/// minute offset <c>m</c> from 09:15 (0 to 374) is centred at <c>x = 70 + 910 × (m + 0.5) / 375</c>; its body is 60 % of a slot wide and its wick 1 unit. <c>y = 390 − 370 × (p − lo) / (hi − lo)</c> where <c>[lo, hi]</c> is the range of
/// the day's lows and highs and of the entry, stop, target (when there is one) and exit prices, widened by 3 % of the range on each side; a flat range is widened to <c>price ± max(0.5 % of price, 0.05)</c>. A candle that closes at or above its open
/// is hollow, one that closes below is filled, so colour is not the only cue; a missing minute is a gap. Coordinates are written with at most 2 decimals.
/// </summary>
public static class CandleSvg
{
    /// <summary>The left edge of the plot area.</summary>
    public const double PlotLeft = 70;

    /// <summary>The right edge of the plot area.</summary>
    public const double PlotRight = 980;

    /// <summary>The top edge of the plot area.</summary>
    public const double PlotTop = 20;

    /// <summary>The bottom edge of the plot area.</summary>
    public const double PlotBottom = 390;

    /// <summary>The minutes of the session (slots on the x axis).</summary>
    public const int Slots = SessionClock.RegularBars;

    private const double PlotWidth = PlotRight - PlotLeft;

    private const double PlotHeight = PlotBottom - PlotTop;

    private const double SlotWidth = PlotWidth / Slots;

    private const double BodyShare = 0.6;

    private const double RangePadding = 0.03;

    private static readonly int[] TickOffsets = [45, 105, 165, 225, 285, 345];

    /// <summary>The y of a price for the range <c>[lo, hi]</c>.</summary>
    public static double Y(double price, double lo, double hi) => PlotBottom - (PlotHeight * (price - lo) / (hi - lo));

    /// <summary>The x of the centre of the slot of minute offset <paramref name="minute"/>.</summary>
    public static double XCentre(int minute) => PlotLeft + (PlotWidth * (minute + 0.5) / Slots);

    /// <summary>The x of the left edge of the slot of minute offset <paramref name="minute"/> (a decision made at the end of minute m − 1 is at the edge of m).</summary>
    public static double XEdge(double minute) => PlotLeft + (PlotWidth * minute / Slots);

    /// <summary>The range of the y axis for a trade: the day's lows and highs and the levels, widened as above.</summary>
    public static (double Lo, double Hi) PriceRange(TradeCardData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var trade = data.Trade.Trade;
        var prices = new List<double> { trade.EntryPrice, trade.StopPrice };
        if (SessionClock.DateOf(trade.ExitEpochSeconds) == trade.TradeDate)
        {
            // an exit on a later day (a swing trade) is not on this chart and must not stretch it
            prices.Add(trade.ExitPrice);
        }

        if (trade.TargetPrice is { } target)
        {
            prices.Add(target);
        }

        foreach (var bar in data.DayBars)
        {
            prices.Add(bar.Low);
            prices.Add(bar.High);
        }

        var lo = prices.Min();
        var hi = prices.Max();
        if (hi == lo)
        {
            var half = Math.Max(0.005 * Math.Abs(lo), 0.05);
            return (lo - half, hi + half);
        }

        var pad = RangePadding * (hi - lo);
        return (lo - pad, hi + pad);
    }

    /// <summary>The SVG of the chart.</summary>
    public static string Render(TradeCardData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var trade = data.Trade.Trade;
        var (lo, hi) = PriceRange(data);
        var open = OpenEpoch(trade.TradeDate);
        var label = CardNames.Ticker(Html.Strip(trade.Symbol)) + " " + ExportSpellings.Of(trade.Side) + " " + CardFormat.Date(trade.TradeDate) + ": 1-minute candles with the decision, entry, stop, target and exit marked";

        var svg = new StringBuilder(64 * 1024);
        svg.Append("<svg viewBox=\"0 0 1000 440\" role=\"img\" aria-label=\"").Append(Html.Attr(label)).Append("\">\n");
        svg.Append("<title>").Append(Html.Text(label)).Append("</title>\n");
        svg.Append("<rect class=\"frame\" x=\"70\" y=\"20\" width=\"910\" height=\"370\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"0.5\"/>\n");
        Grid(svg, lo, hi);
        Ticks(svg);
        Candles(svg, data.DayBars, open, lo, hi);
        Levels(svg, trade, lo, hi);
        Marks(svg, trade, open);
        svg.Append("</svg>");
        return svg.ToString();
    }

    private static long OpenEpoch(DateOnly date) =>
        new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, XDrishti.Domain.Market.IndiaTime.Offset).ToUnixTimeSeconds() + (SessionClock.OpenMinute * 60L);

    private static void Grid(StringBuilder svg, double lo, double hi)
    {
        svg.Append("<g class=\"grid\" stroke=\"currentColor\" stroke-width=\"0.25\" text-anchor=\"end\">\n");
        for (var k = 0; k < 5; k++)
        {
            var price = lo + ((hi - lo) * k / 4);
            var y = CardFormat.Coord(Y(price, lo, hi));
            svg.Append("<line x1=\"70\" y1=\"").Append(y).Append("\" x2=\"980\" y2=\"").Append(y).Append("\"/>\n");
            svg.Append("<text x=\"66\" y=\"").Append(CardFormat.Coord(Y(price, lo, hi) + 3)).Append("\" stroke=\"none\" fill=\"currentColor\">").Append(Html.Text(CardFormat.Price(price))).Append("</text>\n");
        }

        svg.Append("</g>\n");
    }

    private static void Ticks(StringBuilder svg)
    {
        svg.Append("<g class=\"ticks\" stroke=\"currentColor\" stroke-width=\"0.5\" text-anchor=\"middle\">\n");
        foreach (var offset in TickOffsets)
        {
            var x = CardFormat.Coord(XEdge(offset));
            svg.Append("<line x1=\"").Append(x).Append("\" y1=\"390\" x2=\"").Append(x).Append("\" y2=\"396\"/>\n");
            svg.Append("<text x=\"").Append(x).Append("\" y=\"410\" stroke=\"none\" fill=\"currentColor\">").Append(Html.Text(CardFormat.ClockAtOffset(offset))).Append("</text>\n");
        }

        svg.Append("</g>\n");
    }

    private static void Candles(StringBuilder svg, IReadOnlyList<Bar> bars, long open, double lo, double hi)
    {
        var wicks = new StringBuilder();
        var hollow = new StringBuilder();
        var filled = new StringBuilder();
        var bodyWidth = CardFormat.Coord(SlotWidth * BodyShare);
        foreach (var bar in bars)
        {
            var offset = (bar.EpochSeconds - open) / 60;
            if (bar.EpochSeconds < open || (bar.EpochSeconds - open) % 60 != 0 || offset >= Slots)
            {
                continue;
            }

            var x = XCentre((int)offset);
            var yHigh = Y(bar.High, lo, hi);
            var yLow = Y(bar.Low, lo, hi);
            var yTop = Y(Math.Max(bar.Open, bar.Close), lo, hi);
            var yBottom = Y(Math.Min(bar.Open, bar.Close), lo, hi);
            wicks.Append("<line x1=\"").Append(CardFormat.Coord(x)).Append("\" y1=\"").Append(CardFormat.Coord(yHigh)).Append("\" x2=\"").Append(CardFormat.Coord(x)).Append("\" y2=\"").Append(CardFormat.Coord(yLow)).Append("\"/>\n");
            var body = bar.Close >= bar.Open ? hollow : filled;
            body.Append("<rect x=\"").Append(CardFormat.Coord(x - (SlotWidth * BodyShare / 2))).Append("\" y=\"").Append(CardFormat.Coord(yTop)).Append("\" width=\"").Append(bodyWidth).Append("\" height=\"").Append(CardFormat.Coord(Math.Max(yBottom - yTop, 1))).Append("\"/>\n");
        }

        svg.Append("<g class=\"wick\" stroke=\"currentColor\" stroke-width=\"1\">\n").Append(wicks).Append("</g>\n");
        svg.Append("<g class=\"up\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"0.75\">\n").Append(hollow).Append("</g>\n");
        svg.Append("<g class=\"down\" fill=\"currentColor\" stroke=\"currentColor\" stroke-width=\"0.75\">\n").Append(filled).Append("</g>\n");
    }

    private static void Levels(StringBuilder svg, SimTrade trade, double lo, double hi)
    {
        Level(svg, "entry", trade.EntryPrice, lo, hi, string.Empty);
        Level(svg, "stop", trade.StopPrice, lo, hi, " stroke-dasharray=\"6 3\"");
        if (trade.TargetPrice is { } target)
        {
            Level(svg, "target", target, lo, hi, " stroke-dasharray=\"2 3\"");
        }
    }

    private static void Level(StringBuilder svg, string name, double price, double lo, double hi, string dash)
    {
        var y = Y(price, lo, hi);
        var text = y < PlotTop + 12 ? y + 12 : y - 3;
        svg.Append("<g class=\"level ").Append(name).Append("\" stroke=\"currentColor\" stroke-width=\"1.25\" text-anchor=\"end\">\n");
        svg.Append("<line x1=\"70\" y1=\"").Append(CardFormat.Coord(y)).Append("\" x2=\"980\" y2=\"").Append(CardFormat.Coord(y)).Append('"').Append(dash).Append("/>\n");
        svg.Append("<text x=\"976\" y=\"").Append(CardFormat.Coord(text)).Append("\" stroke=\"none\" fill=\"currentColor\">").Append(Html.Text(name + " " + CardFormat.Price(price))).Append("</text>\n");
        svg.Append("</g>\n");
    }

    private static void Marks(StringBuilder svg, SimTrade trade, long open)
    {
        var decision = (trade.DecisionEpochSeconds - open) / 60.0;
        if (decision is >= 0 and <= Slots)
        {
            Mark(svg, "decision", XEdge(decision), CardFormat.Clock(trade.DecisionEpochSeconds), 34);
        }

        var entry = (trade.EntryEpochSeconds - open) / 60;
        if (trade.EntryEpochSeconds >= open && entry < Slots)
        {
            Mark(svg, "entry", XCentre((int)entry), CardFormat.Clock(trade.EntryEpochSeconds), 46);
        }

        var exit = (trade.ExitEpochSeconds - open) / 60;
        if (trade.ExitEpochSeconds >= open && exit < Slots)
        {
            Mark(svg, "exit", XCentre((int)exit), CardFormat.Clock(trade.ExitEpochSeconds), 58);
        }
    }

    private static void Mark(StringBuilder svg, string name, double x, string clock, int labelY)
    {
        var anchor = x > 800 ? "end" : "start";
        var textX = x > 800 ? x - 3 : x + 3;
        svg.Append("<g class=\"mark ").Append(name).Append("\" stroke=\"currentColor\" stroke-width=\"0.75\" stroke-dasharray=\"1 2\" text-anchor=\"").Append(anchor).Append("\">\n");
        svg.Append("<line x1=\"").Append(CardFormat.Coord(x)).Append("\" y1=\"20\" x2=\"").Append(CardFormat.Coord(x)).Append("\" y2=\"390\"/>\n");
        svg.Append("<text x=\"").Append(CardFormat.Coord(textX)).Append("\" y=\"").Append(labelY.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\" stroke=\"none\" fill=\"currentColor\">").Append(Html.Text(name + " " + clock)).Append("</text>\n");
        svg.Append("</g>\n");
    }
}
