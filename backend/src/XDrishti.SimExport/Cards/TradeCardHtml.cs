using System.Buffers;
using System.Text;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Cards;

/// <summary>
/// One trade card as a static HTML file (spec P3-04c, C3 and C5): the banner of the run, the title, the chart, the timeline, the arithmetic of R with every operand printed, the costs when the run was costed, the rule, the line saying that
/// the conditions at the decision bar come with P3-04d, the stored 09:15 bar, the data notes of the instrument and the run key. No script, no network reference, every dynamic string through <see cref="Html.Text"/>, no clock, no culture: two
/// calls with equal arguments write the same bytes. The card prints no figure about the whole run: only the numbers of its own trade (a performance statistic is a claim a single trade cannot support).
/// </summary>
public static class TradeCardHtml
{
    /// <summary>Writes the card of <paramref name="data"/> as UTF-8 to the sink.</summary>
    /// <param name="sink">Where the bytes go.</param>
    /// <param name="data">The trade and the bars of its day.</param>
    /// <param name="run">The run the trade belongs to (its key, its strategy and its engine version are printed).</param>
    /// <param name="labels">The labels of the run, as <see cref="RunLabels.Of"/> builds them.</param>
    public static void Write(IBufferWriter<byte> sink, TradeCardData data, SimulationRun run, RunLabelSet labels)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(labels);

        var trade = data.Trade.Trade;
        var title = Title(trade);
        var page = CardPage.Begin(title, labels);
        page.Append("<main>\n<h1>").Append(Html.Text(title)).Append("</h1>\n");
        Chart(page, data);
        Timeline(page, trade);
        Arithmetic(page, trade);
        if (trade.Costing is { } costing)
        {
            Costs(page, trade, costing);
        }

        Rule(page, run.Strategy, trade.Side);
        page.Append("<h2>conditions at the decision bar</h2>\n<p>conditions at the decision bar: not in this export (P3-04d)</p>\n");
        OpeningBar(page, data);
        DataNotes(page, data.Instrument);
        Identity(page, run, trade);
        page.Append("</main>\n");
        CardPage.End(page, sink);
    }

    /// <summary>The title: <c>NSE:RELIANCE BUY 2025-03-04, target, exact result R +2.000</c>.</summary>
    public static string Title(SimTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        return trade.Symbol + " " + ExportSpellings.Of(trade.Side) + " " + CardFormat.Date(trade.TradeDate) + ", " + ExportSpellings.Of(trade.ExitReason) + ", exact result R " + CardFormat.SignedR(trade.RExact);
    }

    private static void Chart(StringBuilder page, TradeCardData data)
    {
        page.Append("<h2>chart</h2>\n");
        page.Append("<p class=\"note\">1-minute candles of the trade day, India time, in prices adjusted for the corporate actions the run applied (the prices the replay used). Hollow: close at or above open. Filled: close below open. A gap is a minute with no stored bar.</p>\n");
        page.Append(CandleSvg.Render(data)).Append('\n');
    }

    private static void Timeline(StringBuilder page, SimTrade trade)
    {
        page.Append("<h2>timeline</h2>\n<table>\n<thead>");
        CardPage.Row(page, "th", "step", "time (India)", "exact price", "filled price", "note");
        page.Append("</thead>\n<tbody>\n");
        CardPage.Row(page, "td", "decision", CardFormat.Clock(trade.DecisionEpochSeconds), string.Empty, string.Empty, "the end of the trigger bar; the order is live from this instant");
        var bar = trade.TriggerBar;
        CardPage.Row(
            page,
            "td",
            "trigger bar",
            CardFormat.Clock(bar.EpochSeconds),
            string.Empty,
            string.Empty,
            "timeframe " + ExportSpellings.Of(trade.TriggerTimeframe) + ", open " + CardFormat.Price(bar.Open) + ", high " + CardFormat.Price(bar.High) + ", low " + CardFormat.Price(bar.Low) + ", close " + CardFormat.Price(bar.Close)
            + ", volume " + CardFormat.Int(bar.Volume) + ", minutes held " + CardFormat.Int(trade.TriggerBarMinutes) + ", complete " + (trade.TriggerBarComplete ? "yes" : "no"));
        CardPage.Row(
            page,
            "td",
            "entry",
            CardFormat.Clock(trade.EntryEpochSeconds),
            CardFormat.Price(trade.EntryPrice),
            CardFormat.Price(trade.EntryFilledPrice),
            "fill minute; the limit order filled " + (trade.EntryFill == EntryFill.AtOpen ? "at the open of the minute" : "at the limit"));
        CardPage.Row(page, "td", "stop", string.Empty, CardFormat.Price(trade.StopPrice), string.Empty, "the level as the strategy gave it, not rounded to the tick");
        CardPage.Row(page, "td", "target", string.Empty, trade.TargetPrice is { } target ? CardFormat.Price(target) : "none", string.Empty, trade.TargetPrice is null ? "this strategy has no fixed target" : "the level, not rounded to the tick");
        var exitDate = CardFormat.DateOf(trade.ExitEpochSeconds);
        CardPage.Row(
            page,
            "td",
            "exit",
            (exitDate == trade.TradeDate ? string.Empty : CardFormat.Date(exitDate) + " ") + CardFormat.Clock(trade.ExitEpochSeconds),
            CardFormat.Price(trade.ExitPrice),
            CardFormat.Price(trade.ExitFilledPrice),
            "reason " + ExportSpellings.Of(trade.ExitReason) + (exitDate == trade.TradeDate ? string.Empty : "; the exit is on a later day than the chart shows"));
        page.Append("</tbody>\n</table>\n");
        page.Append("<p class=\"note\">fills: ").Append(Html.Text(ExportSpellings.Of(trade.Fills))).Append("; tick from the NSE default: ").Append(trade.TickFallback ? "yes" : "no").Append("; sessions held ").Append(Html.Text(CardFormat.Int(trade.SessionsHeld))).Append("</p>\n");
    }

    private static void Arithmetic(StringBuilder page, SimTrade trade)
    {
        var sign = trade.Side == Side.Buy ? 1 : -1;
        var signText = sign > 0 ? "+1" : "-1";
        var risk = Math.Abs(trade.EntryPrice - trade.StopPrice);
        page.Append("<h2>arithmetic</h2>\n<pre>");
        var lines = new[]
        {
            "side = " + signText + " (" + ExportSpellings.Of(trade.Side) + ")",
            "risk = |entry − stop| = |" + CardFormat.Price(trade.EntryPrice) + " − " + CardFormat.Price(trade.StopPrice) + "| = " + CardFormat.Price(risk),
            "exact result R = side × (exit − entry) / risk = " + signText + " × (" + CardFormat.Price(trade.ExitPrice) + " − " + CardFormat.Price(trade.EntryPrice) + ") / " + CardFormat.Price(risk) + " = " + CardFormat.SignedR(trade.RExact),
            "filled result R = side × (exit filled − entry filled) / risk = " + signText + " × (" + CardFormat.Price(trade.ExitFilledPrice) + " − " + CardFormat.Price(trade.EntryFilledPrice) + ") / " + CardFormat.Price(risk) + " = " + CardFormat.SignedR(trade.RFilled),
            "slippage R = filled result R − exact result R = " + CardFormat.SignedR(trade.RFilled) + " − " + CardFormat.SignedR(trade.RExact) + " = " + CardFormat.SignedR(trade.SlippageR),
            "adverse excursion R (MAE) = " + CardFormat.R(trade.MaeR) + ", in price = MAE R × risk = " + CardFormat.R(trade.MaeR) + " × " + CardFormat.Price(risk) + " = " + CardFormat.Price(trade.MaeR * risk),
            "favourable excursion R (MFE) = " + CardFormat.R(trade.MfeR) + ", in price = MFE R × risk = " + CardFormat.R(trade.MfeR) + " × " + CardFormat.Price(risk) + " = " + CardFormat.Price(trade.MfeR * risk),
        };
        page.Append(Html.Lines(lines)).Append("</pre>\n<p class=\"note\">each figure is rounded after the calculation</p>\n");
    }

    private static void Costs(StringBuilder page, SimTrade trade, TradeCosting costing)
    {
        page.Append("<h2>costs</h2>\n");
        page.Append("<p class=\"note\">sheet ").Append(Html.Text(costing.SheetId)).Append(", segment ").Append(Html.Text(ExportSpellings.Of(costing.Segment))).Append(", quantity ").Append(Html.Text(CardFormat.Int(costing.Quantity)))
            .Append(" shares, risk taken ").Append(Html.Text(CardFormat.Money(costing.RiskTaken))).Append(" rupees (quantity × risk), entry turnover ").Append(Html.Text(CardFormat.Money(costing.EntryTurnover)))
            .Append(", exit turnover ").Append(Html.Text(CardFormat.Money(costing.ExitTurnover))).Append("</p>\n");
        page.Append("<table>\n<thead>");
        CardPage.Row(page, "th", "charge (rupees)", "entry order", "exit order");
        page.Append("</thead>\n<tbody>\n");
        var entry = costing.EntryLeg;
        var exit = costing.ExitLeg;
        CardPage.Row(page, "td", "brokerage", CardFormat.Money(entry.Brokerage), CardFormat.Money(exit.Brokerage));
        CardPage.Row(page, "td", "securities transaction tax", CardFormat.Money(entry.Stt), CardFormat.Money(exit.Stt));
        CardPage.Row(page, "td", "exchange transaction charge", CardFormat.Money(entry.ExchangeTxn), CardFormat.Money(exit.ExchangeTxn));
        CardPage.Row(page, "td", "SEBI fee", CardFormat.Money(entry.SebiFee), CardFormat.Money(exit.SebiFee));
        CardPage.Row(page, "td", "stamp duty", CardFormat.Money(entry.StampDuty), CardFormat.Money(exit.StampDuty));
        CardPage.Row(page, "td", "other", CardFormat.Money(entry.OtherCharges), CardFormat.Money(exit.OtherCharges));
        CardPage.Row(page, "td", "depository charge", CardFormat.Money(entry.DpCharge), CardFormat.Money(exit.DpCharge));
        CardPage.Row(page, "td", "GST", CardFormat.Money(entry.Gst), CardFormat.Money(exit.Gst));
        CardPage.Row(page, "td", "this order", CardFormat.Money(entry.Total), CardFormat.Money(exit.Total));
        page.Append("</tbody>\n</table>\n<pre>");
        var lines = new[]
        {
            "all 16 lines added = " + CardFormat.Money(entry.Total) + " + " + CardFormat.Money(exit.Total) + " = " + CardFormat.Money(costing.TotalCost) + " rupees",
            "cost R = charges / risk taken = " + CardFormat.Money(costing.TotalCost) + " / " + CardFormat.Money(costing.RiskTaken) + " = " + CardFormat.R(costing.CostR),
            "net result R = filled result R − cost R = " + CardFormat.SignedR(trade.RFilled) + " − " + CardFormat.R(costing.CostR) + " = " + CardFormat.SignedR(costing.RNet),
        };
        page.Append(Html.Lines(lines)).Append("</pre>\n<p class=\"note\">each figure is rounded after the calculation</p>\n");
    }

    private static void Rule(StringBuilder page, StrategyRecord strategy, Side side)
    {
        var view = RuleView.Of(strategy, side);
        page.Append("<h2>rule (").Append(Html.Text(ExportSpellings.Of(side))).Append(")</h2>\n");
        if (view.IsFallback)
        {
            page.Append("<p class=\"note\">The recorded definition could not be read as a rule for this side, so it is printed as stored. SHA-256 ").Append(Html.Text(view.Sha256)).Append("</p>\n<pre>").Append(Html.Lines(view.Raw.Split('\n'))).Append("</pre>\n");
            return;
        }

        page.Append("<pre>").Append(Html.Lines(view.Lines)).Append("</pre>\n");
    }

    private static void OpeningBar(StringBuilder page, TradeCardData data)
    {
        page.Append("<h2>stored 09:15 bar</h2>\n");
        if (data.OpeningBar is not { } opening)
        {
            page.Append("<p>no 09:15 bar stored for this day</p>\n");
        }
        else
        {
            page.Append("<table>\n<thead>");
            CardPage.Row(page, "th", "prices", "open", "high", "low", "close", "volume");
            page.Append("</thead>\n<tbody>\n");
            CardPage.Row(page, "td", "as on the chart (adjusted)", CardFormat.Price(opening.Open), CardFormat.Price(opening.High), CardFormat.Price(opening.Low), CardFormat.Price(opening.Close), CardFormat.Int(opening.Volume));
            if (data.OpeningBarRaw is { } raw)
            {
                CardPage.Row(page, "td", "as stored (raw)", CardFormat.Price(raw.Open), CardFormat.Price(raw.High), CardFormat.Price(raw.Low), CardFormat.Price(raw.Close), CardFormat.Int(raw.Volume));
            }

            page.Append("</tbody>\n</table>\n");
        }

        page.Append("<p class=\"note\">The CSV importer widens an inconsistent opening high or low, so the stored 09:15 bar may differ from the bar of a vendor chart (ISS-21).</p>\n");
    }

    private static void DataNotes(StringBuilder page, InstrumentRun instrument)
    {
        page.Append("<h2>data notes</h2>\n<ul>\n");
        Item(page, "instrument: " + instrument.Symbol);
        Item(page, "adjustments applied: " + (instrument.Adjustments.Count == 0 ? "none" : string.Join("; ", instrument.Adjustments.Select(Adjustment))));
        Item(page, "flagged days: " + (instrument.Discontinuities.Count == 0 ? "none" : string.Join("; ", instrument.Discontinuities.Select(d => CardFormat.Date(d.Date) + " (first open is " + CardFormat.Price(d.Ratio) + " times the previous close)"))));
        var sessions = instrument.Sessions;
        Item(page, "sessions: " + CardFormat.Int(sessions.Ran) + " ran; left out: closed day " + CardFormat.Int(sessions.ClosedDay) + ", special session " + CardFormat.Int(sessions.SpecialSession) + ", short session " + CardFormat.Int(sessions.ShortSession) + ", missing session " + CardFormat.Int(sessions.MissingSession));
        Item(page, instrument.TickSize is { } tick ? "tick: " + CardFormat.Decimal(tick) + " rupee, " + (instrument.TickFromFallback ? "the NSE default (the master has none)" : "from the instrument master") : "tick: none used");
        Item(page, "bar-stream hash (first 12 hex): " + (instrument.BarsSha256 is { Length: >= 12 } hash ? hash[..12] : "none") + "; bars read " + CardFormat.Int(instrument.BarsRead));
        page.Append("</ul>\n");
    }

    private static string Adjustment(AppliedAdjustment a) =>
        ExportSpellings.Of(a.Kind) + " ex-date " + CardFormat.Date(a.ExDate) + ", " + CardFormat.Int(a.Numerator) + ":" + CardFormat.Int(a.Denominator) + ", factor " + CardFormat.Decimal(a.Factor);

    private static void Identity(StringBuilder page, SimulationRun run, SimTrade trade)
    {
        page.Append("<h2>run</h2>\n<ul>\n");
        Item(page, "run key: " + run.RunKey);
        Item(page, "strategy: " + run.Strategy.Id + ", engine " + run.EngineVersion);
        Item(page, "trade key: " + trade.Key);
        page.Append("</ul>\n");
    }

    private static void Item(StringBuilder page, string text) => page.Append("<li>").Append(Html.Text(text)).Append("</li>\n");
}
