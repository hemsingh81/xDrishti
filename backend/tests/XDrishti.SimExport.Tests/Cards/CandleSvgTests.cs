using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Cards;

/// <summary>Criterion 4 of P3-04c, the chart: the fixed geometry of C4 read back from the SVG.</summary>
public sealed class CandleSvgTests
{
    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    private static IReadOnlyList<HtmlToken> Tokens(string svg) => HtmlReader.Read(svg);

    /// <summary>The y of the line inside the group <c>level &lt;name&gt;</c>.</summary>
    private static double LevelY(IReadOnlyList<HtmlToken> tokens, string name)
    {
        var group = tokens.OfType<HtmlToken.Start>().ToList().FindIndex(t => t.Name == "g" && t.Attr("class") == "level " + name);
        group.ShouldBeGreaterThanOrEqualTo(0, "the " + name + " level is drawn");
        var line = tokens.OfType<HtmlToken.Start>().Skip(group + 1).First(t => t.Name == "line");
        line.Attr("y1").ShouldBe(line.Attr("y2"));
        return Number(line.Attr("y1")!);
    }

    private static (SimTrade Trade, Bar[] Bars) Flat(double price)
    {
        var date = CardFixtures.Date;
        var bars = Day.Dense(date).Start(price).Bars();
        var trade = CostTrades.Make(Side.Buy, date, price, price - 1, price + 1, null, null, null, ExitReason.SquareOff, 600, 630) with { StopPrice = price, ExitPrice = price, TargetPrice = null, DecisionEpochSeconds = Day.Epoch(date, 600) };
        return (trade, bars);
    }

    [Fact]
    public void The_svg_has_the_fixed_view_box_no_namespace_an_image_role_a_label_and_a_title()
    {
        var (run, data) = Card();

        var svg = CandleSvg.Render(data);

        svg.ShouldStartWith("<svg viewBox=\"0 0 1000 440\" role=\"img\" aria-label=\"KESTREL BUY 2026-03-03: ");
        svg.ShouldNotContain("xmlns");
        svg.ShouldContain("<title>KESTREL BUY 2026-03-03: ");
        run.Trades.Count.ShouldBe(1);
    }

    private static (SimulationRun Run, TradeCardData Data) Card()
    {
        var (trade, bars) = CardFixtures.BuyAtTarget();
        return CardFixtures.Card(trade, bars);
    }

    [Fact]
    public void The_entry_line_is_at_the_y_of_the_entry_price_and_the_stop_line_at_the_y_of_the_stop_and_the_target_too()
    {
        var (_, data) = Card();
        var (lo, hi) = CandleSvg.PriceRange(data);
        var tokens = Tokens(CandleSvg.Render(data));

        LevelY(tokens, "entry").ShouldBe(Math.Round(CandleSvg.Y(100.5, lo, hi), 2), 0.011);
        LevelY(tokens, "stop").ShouldBe(Math.Round(CandleSvg.Y(98, lo, hi), 2), 0.011);
        LevelY(tokens, "target").ShouldBe(Math.Round(CandleSvg.Y(105.5, lo, hi), 2), 0.011);
        LevelY(tokens, "stop").ShouldBeGreaterThan(LevelY(tokens, "entry"), "a lower price is lower on the page: a larger y");
    }

    [Fact]
    public void The_y_axis_follows_the_formula_with_the_range_widened_by_three_percent_each_side()
    {
        var (_, data) = Card();

        var (lo, hi) = CandleSvg.PriceRange(data);

        var low = Math.Min(98, data.DayBars.Min(b => b.Low));
        var high = Math.Max(105.5, data.DayBars.Max(b => b.High));
        lo.ShouldBe(low - (0.03 * (high - low)), 1e-9);
        hi.ShouldBe(high + (0.03 * (high - low)), 1e-9);
        CandleSvg.Y(lo, lo, hi).ShouldBe(390, 1e-9);
        CandleSvg.Y(hi, lo, hi).ShouldBe(20, 1e-9);
        CandleSvg.XCentre(0).ShouldBe(70 + (910 * 0.5 / 375), 1e-9);
        CandleSvg.XCentre(374).ShouldBe(70 + (910 * 374.5 / 375), 1e-9);
    }

    [Fact]
    public void A_stop_outside_the_days_range_is_inside_the_plot_and_widens_the_range()
    {
        var (trade, bars) = CardFixtures.SellAtStop();
        var (_, data) = CardFixtures.Card(trade, bars);

        var (lo, hi) = CandleSvg.PriceRange(data);

        bars.Max(b => b.High).ShouldBeLessThan(204, "the stop is above the day's range");
        hi.ShouldBeGreaterThan(204);
        var y = LevelY(Tokens(CandleSvg.Render(data)), "stop");
        y.ShouldBeInRange(20, 390);
        lo.ShouldBeLessThan(192, "the target is below the day's range too");
    }

    [Fact]
    public void A_flat_range_is_widened_by_half_a_percent_or_five_paise_and_nothing_divides_by_zero()
    {
        var (trade, bars) = Flat(200);
        var (_, data) = CardFixtures.Card(trade, bars);

        var (lo, hi) = CandleSvg.PriceRange(data);
        var svg = CandleSvg.Render(data);

        lo.ShouldBe(199.0, 1e-9, "0.5 % of 200 is 1");
        hi.ShouldBe(201.0, 1e-9);
        svg.ShouldNotContain("NaN");
        svg.ShouldNotContain("Infinity");
        var (cheap, cheapBars) = Flat(2);
        var (_, cheapData) = CardFixtures.Card(cheap, cheapBars);
        var (cheapLo, cheapHi) = CandleSvg.PriceRange(cheapData);
        cheapLo.ShouldBe(1.95, 1e-9, "0.5 % of 2 is below 0.05, so 0.05 is used");
        cheapHi.ShouldBe(2.05, 1e-9);
    }

    [Fact]
    public void Up_candles_are_hollow_and_down_candles_are_filled_and_a_missing_minute_is_a_gap()
    {
        var (trade, bars) = CardFixtures.SellAtStop();
        bars = [.. bars.Select(b => b.EpochSeconds == Day.Epoch(CardFixtures.Date, 560) ? b with { Open = 200.4, High = 200.5, Low = 199.5, Close = 199.6 } : b)];
        var (_, data) = CardFixtures.Card(trade, bars);

        var tokens = Tokens(CandleSvg.Render(data)).OfType<HtmlToken.Start>().ToList();

        var up = tokens.Single(t => t.Name == "g" && t.Attr("class") == "up");
        var down = tokens.Single(t => t.Name == "g" && t.Attr("class") == "down");
        up.Attr("fill").ShouldBe("none");
        down.Attr("fill").ShouldBe("currentColor");
        var wicks = tokens.SkipWhile(t => t.Attr("class") != "wick").Skip(1).TakeWhile(t => t.Name == "line").Count();
        wicks.ShouldBe(bars.Length);
        bars.Length.ShouldBe(374, "the day lacks 10:20");
        var rects = tokens.Where(t => t.Name == "rect" && t.Attr("class") is null).ToList();
        rects.Count.ShouldBe(bars.Length);
    }

    [Fact]
    public void The_candle_of_minute_zero_is_centred_in_its_slot_and_the_body_is_sixty_percent_of_it()
    {
        var (_, data) = Card();

        var tokens = Tokens(CandleSvg.Render(data)).OfType<HtmlToken.Start>().ToList();

        var wick = tokens.SkipWhile(t => t.Attr("class") != "wick").Skip(1).First(t => t.Name == "line");
        Number(wick.Attr("x1")!).ShouldBe(Math.Round(CandleSvg.XCentre(0), 2), 0.011);
        var body = tokens.First(t => t.Name == "rect" && t.Attr("class") is null);
        Number(body.Attr("width")!).ShouldBe(Math.Round(910.0 / 375 * 0.6, 2), 0.011);
    }

    [Fact]
    public void There_are_five_gridlines_and_six_time_ticks_from_ten_to_three_and_the_marks_are_where_the_times_are()
    {
        var (_, data) = Card();

        var svg = CandleSvg.Render(data);
        var tokens = Tokens(svg);
        var text = HtmlReader.VisibleText(tokens);

        foreach (var clock in new[] { "10:00", "11:00", "12:00", "13:00", "14:00", "15:00" })
        {
            text.ShouldContain(clock);
        }

        var starts = tokens.OfType<HtmlToken.Start>().ToList();
        var grid = starts.FindIndex(t => t.Attr("class") == "grid");
        starts.Skip(grid + 1).TakeWhile(t => t.Attr("class") != "ticks").Count(t => t.Name == "line").ShouldBe(5);
        text.ShouldContain("decision 09:45");
        text.ShouldContain("entry 09:45");
        text.ShouldContain("exit 11:10");
        var decision = starts.SkipWhile(t => t.Attr("class") != "mark decision").Skip(1).First(t => t.Name == "line");
        Number(decision.Attr("x1")!).ShouldBe(Math.Round(CandleSvg.XEdge(30), 2), 0.011, "the end of the trigger minute 09:44 is the edge of the slot of 09:45, offset 30");
        var entry = starts.SkipWhile(t => t.Attr("class") != "mark entry").Skip(1).First(t => t.Name == "line");
        Number(entry.Attr("x1")!).ShouldBe(Math.Round(CandleSvg.XCentre(30), 2), 0.011);
    }

    [Fact]
    public void An_exit_on_a_later_day_has_no_exit_mark_and_the_svg_is_well_formed()
    {
        var (trade, bars) = CardFixtures.BuyAtTarget();
        trade = trade with { ExitEpochSeconds = Day.Epoch(CardFixtures.Date.AddDays(1), 600) };
        var (_, data) = CardFixtures.Card(trade, bars);

        var svg = CandleSvg.Render(data);

        svg.ShouldNotContain("mark exit");
        var near = CandleSvg.PriceRange(data);
        var (_, sameDay) = CardFixtures.Card(trade with { ExitEpochSeconds = Day.Epoch(CardFixtures.Date, 600), ExitPrice = 500 }, bars);
        CandleSvg.PriceRange(sameDay).Hi.ShouldBeGreaterThan(500, "an exit on the chart's day stretches the range");
        var (_, swing) = CardFixtures.Card(trade with { ExitPrice = 500 }, bars);
        CandleSvg.PriceRange(swing).ShouldBe(near, "an exit on a later day does not");
        Should.NotThrow(() => HtmlReader.Read(svg));
    }

    [Fact]
    public void The_chart_of_a_full_day_is_small()
    {
        var (_, data) = Card();

        System.Text.Encoding.UTF8.GetByteCount(CandleSvg.Render(data)).ShouldBeLessThan(60_000);
    }

    [Fact]
    public void A_day_with_no_bars_still_draws_the_levels()
    {
        var (trade, _) = CardFixtures.BuyAtTarget();
        var (_, data) = CardFixtures.Card(trade, []);

        var svg = CandleSvg.Render(data);

        svg.ShouldNotContain("NaN");
        LevelY(Tokens(svg), "entry").ShouldBeInRange(20, 390);
    }
}
