using System.Globalization;
using System.Text.RegularExpressions;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Cards;

/// <summary>Criteria 4, 5 and 6 of P3-04c: the card (goldens, the arithmetic, the sections), the escaping and the scan, and the absence of statistics.</summary>
public sealed partial class TradeCardHtmlTests
{
    [GeneratedRegex(@"risk = \|entry − stop\| = \|(?<e>[-0-9.]+) − (?<s>[-0-9.]+)\| = (?<r>[-0-9.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex RiskLine();

    [GeneratedRegex(@"exact result R = side × \(exit − entry\) / risk = (?<sign>[+-]1) × \((?<x>[-0-9.]+) − (?<e>[-0-9.]+)\) / (?<r>[-0-9.]+) = (?<v>[-+0-9.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ExactLine();

    [GeneratedRegex(@"filled result R = side × \(exit filled − entry filled\) / risk = (?<sign>[+-]1) × \((?<x>[-0-9.]+) − (?<e>[-0-9.]+)\) / (?<r>[-0-9.]+) = (?<v>[-+0-9.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex FilledLine();

    private static double D(string text) => double.Parse(text, CultureInfo.InvariantCulture);

    // goldens

    [Fact]
    public void The_buy_at_the_target_on_a_split_instrument_equals_the_golden_file_golden()
    {
        var (_, run, data) = CardFixtures.Goldens()[0];

        Golden.CheckCard("card-buy-at-target-split", CardFixtures.Render(run, data));
    }

    [Fact]
    public void The_sell_at_the_stop_on_a_day_with_a_missing_minute_equals_the_golden_file_golden()
    {
        var (_, run, data) = CardFixtures.Goldens()[1];

        Golden.CheckCard("card-sell-at-stop-missing-minute", CardFixtures.Render(run, data));
    }

    [Fact]
    public void The_costed_sell_equals_the_golden_file_and_shows_the_sixteen_lines_golden()
    {
        var (run, data) = CardFixtures.CostedSell();

        var html = CardFixtures.Render(run, data);

        Golden.CheckCard("card-sell-costed", html);
        html.ShouldContain("<h2>costs</h2>");
        html.ShouldContain("all 16 lines added = ");
    }

    [Fact]
    public void A_gross_card_has_no_costs_section()
    {
        var (_, run, data) = CardFixtures.Goldens()[0];

        CardFixtures.Render(run, data).ShouldNotContain("<h2>costs</h2>");
    }

    [Fact]
    public void Two_renders_write_the_same_bytes()
    {
        var (_, run, data) = CardFixtures.Goldens()[0];

        Sinks.Bytes(s => TradeCardHtml.Write(s, data, run, CardFixtures.Labels(run))).ShouldBe(Sinks.Bytes(s => TradeCardHtml.Write(s, data, run, CardFixtures.Labels(run))));
    }

    // the sections

    [Fact]
    public void The_card_has_the_banner_then_the_title_the_chart_the_timeline_the_arithmetic_the_rule_the_p3_04d_line_the_opening_bar_the_data_notes_and_the_run_key()
    {
        var (_, run, data) = CardFixtures.Goldens()[0];

        var html = CardFixtures.Render(run, data);

        var order = new[]
        {
            "<header class=\"banner\">", "exploration: signal-level mechanics", "<details>", "not modelled: ", "</header>", "<h1>NSE:KESTREL BUY 2026-03-03, target, exact result R +2.000</h1>", "<h2>chart</h2>", "<svg viewBox",
            "<h2>timeline</h2>", "<h2>arithmetic</h2>", "<h2>rule (BUY)</h2>", "strategy: ORB (or_min = 15, rr = 2)", "conditions at the decision bar: not in this export (P3-04d)", "<h2>stored 09:15 bar</h2>", "(ISS-21)",
            "<h2>data notes</h2>", "<h2>run</h2>", "run key: " + run.RunKey,
        };
        var at = -1;
        foreach (var part in order)
        {
            var next = html.IndexOf(part, at + 1, StringComparison.Ordinal);
            next.ShouldBeGreaterThan(at, "'" + part + "' comes after the previous section");
            at = next;
        }
    }

    [Fact]
    public void The_banner_shows_the_visible_labels_and_puts_the_limits_in_a_details_element()
    {
        var (_, run, data) = CardFixtures.Goldens()[0];
        var labels = CardFixtures.Labels(run);

        var html = CardFixtures.Render(run, data);

        var banner = html[html.IndexOf("<header class=\"banner\">", StringComparison.Ordinal)..html.IndexOf("</header>", StringComparison.Ordinal)];
        var details = banner[banner.IndexOf("<details>", StringComparison.Ordinal)..];
        foreach (var line in labels.Lines)
        {
            banner.ShouldContain(Html.Text(line));
            details.ShouldNotContain(Html.Text(line));
        }

        foreach (var limit in labels.NotModelled)
        {
            details.ShouldContain(Html.Text("not modelled: " + limit.Code + ": " + limit.Text));
        }
    }

    [Fact]
    public void The_split_card_prints_the_raw_09_15_bar_beside_the_adjusted_one_and_the_adjustment_in_the_data_notes()
    {
        var (_, run, data) = CardFixtures.Goldens()[0];

        var html = CardFixtures.Render(run, data);

        html.ShouldContain("as on the chart (adjusted)");
        html.ShouldContain("as stored (raw)");
        data.OpeningBarRaw.ShouldNotBeNull();
        data.OpeningBar!.Value.Open.ShouldBe(data.OpeningBarRaw.Value.Open * 0.5, 1e-9);
        html.ShouldContain("adjustments applied: split ex-date 2026-03-04, 1:2, factor 0.5");
        html.ShouldContain("bar-stream hash (first 12 hex): 0123456789ab");
        html.ShouldContain("tick: 0.05 rupee, from the instrument master");
    }

    [Fact]
    public void A_day_with_no_09_15_bar_says_so_and_prints_no_other_minute_under_that_heading()
    {
        var (trade, bars) = CardFixtures.BuyAtTarget();
        var (run, data) = CardFixtures.Card(trade, [.. bars.Where(b => b.EpochSeconds != Day.Epoch(CardFixtures.Date, 555))]);

        var html = CardFixtures.Render(run, data);

        data.OpeningBar.ShouldBeNull();
        html.ShouldContain("<p>no 09:15 bar stored for this day</p>");
        html.ShouldNotContain("as on the chart (adjusted)");
        html.ShouldContain("(ISS-21)");
    }

    [Fact]
    public void The_arithmetic_can_be_redone_from_the_printed_prices_and_equals_the_trades_r_to_the_printed_precision()
    {
        foreach (var (name, run, data) in CardFixtures.Goldens())
        {
            var trade = data.Trade.Trade;
            var html = HtmlReader.VisibleText(HtmlReader.Read(CardFixtures.Render(run, data)));

            var risk = RiskLine().Match(html);
            var exact = ExactLine().Match(html);
            var filled = FilledLine().Match(html);

            risk.Success.ShouldBeTrue(name);
            exact.Success.ShouldBeTrue(name);
            filled.Success.ShouldBeTrue(name);
            var printedRisk = Math.Abs(D(risk.Groups["e"].Value) - D(risk.Groups["s"].Value));
            D(risk.Groups["r"].Value).ShouldBe(printedRisk, 1e-4, name);
            var sign = D(exact.Groups["sign"].Value);
            (sign * (D(exact.Groups["x"].Value) - D(exact.Groups["e"].Value)) / printedRisk).ShouldBe(trade.RExact, 0.002, name);
            (sign * (D(filled.Groups["x"].Value) - D(filled.Groups["e"].Value)) / printedRisk).ShouldBe(trade.RFilled, 0.002, name);
            D(exact.Groups["v"].Value).ShouldBe(trade.RExact, 0.0005, name);
            D(filled.Groups["v"].Value).ShouldBe(trade.RFilled, 0.0005, name);
        }
    }

    [Fact]
    public void The_printed_prices_are_those_of_the_record_with_at_most_four_decimals()
    {
        var (_, run, data) = CardFixtures.Goldens()[1];

        var html = CardFixtures.Render(run, data);

        html.ShouldContain("<td>entry</td><td>10:00</td><td>200</td><td>199.9</td>");
        html.ShouldContain("<td>stop</td><td></td><td>204</td>");
        html.ShouldContain("<td>exit</td><td>10:30</td><td>204</td><td>204.1</td><td>reason stop</td>");
        html.ShouldContain("<td>target</td><td></td><td>192</td>");
    }

    [Fact]
    public void The_size_of_a_card_is_under_150_kb()
    {
        foreach (var (_, run, data) in CardFixtures.Goldens())
        {
            Sinks.Bytes(s => TradeCardHtml.Write(s, data, run, CardFixtures.Labels(run))).Length.ShouldBeLessThan(150 * 1024);
        }
    }

    [Fact]
    public void A_definition_that_does_not_parse_prints_the_raw_text_escaped_with_its_hash_and_says_so()
    {
        var (trade, bars) = CardFixtures.BuyAtTarget();
        var (run, data) = CardFixtures.Card(trade, bars, strategy: CardFixtures.Orb() with { DefinitionJson = "<not json>" });

        var html = CardFixtures.Render(run, data);

        html.ShouldContain("could not be read as a rule for this side, so it is printed as stored. SHA-256 " + new string('c', 64));
        html.ShouldContain("<pre>&lt;not json&gt;</pre>");
    }

    [Fact]
    public void A_multi_line_raw_definition_keeps_its_lines_and_the_arithmetic_says_figures_are_rounded_after_the_calculation()
    {
        var (trade, bars) = CardFixtures.BuyAtTarget();
        var (run, data) = CardFixtures.Card(trade, bars, strategy: CardFixtures.Orb() with { DefinitionJson = "not\njson\r\n\tat all" });

        var html = CardFixtures.Render(run, data);

        html.ShouldContain("<pre>not\njson \n at all</pre>");
        html.ShouldContain("each figure is rounded after the calculation");
    }

    // the escaping and the scan

    private static (SimulationRun Run, TradeCardData Data) Hostile()
    {
        const string Payload = "<script>alert(1)</script>\"><img src=x onerror=alert(1)>&amp;' onmouseover='x</svg><script>\0\u202E\u200B";
        var (trade, bars) = CardFixtures.BuyAtTarget();
        trade = trade with { Symbol = "NSE:" + Payload };
        var definition = "{\"buy\":{\"trigger\":{\"mode\":\"all\",\"items\":[" + System.Text.Json.JsonSerializer.Serialize(Payload) + "]},\"entry\":{\"kind\":\"at_close\"}},\"filters\":[" + System.Text.Json.JsonSerializer.Serialize("note http://x " + Payload) + "]}";
        var (run, data) = CardFixtures.Card(trade, bars, strategy: CardFixtures.Orb() with { DefinitionJson = definition, Id = Payload });
        var instrument = data.Instrument with { Symbol = "NSE:" + Payload, SkipDetail = Payload };
        return (run, data with { Instrument = instrument });
    }

    [Fact]
    public void Hostile_text_in_a_symbol_a_rule_item_and_a_note_appears_only_as_escaped_text_and_the_scan_finds_nothing()
    {
        var (run, data) = Hostile();

        var html = CardFixtures.Render(run, data);

        CardScan.Problems(html, isIndex: false).ShouldBeEmpty();
        html.ShouldNotContain("<script");
        html.ShouldNotContain("<img");
        html.ShouldNotContain("onerror=alert(1)>");
        html.ShouldContain("&lt;script&gt;alert(1)&lt;/script&gt;");
        html.ShouldContain("&amp;amp;");
        html.ShouldContain("&#39; onmouseover=&#39;x");
        html.ShouldContain("note http://x ");
        html.ShouldNotContain('\0');
        html.ShouldNotContain('\u202E');
        html.ShouldNotContain('\u200B');
    }

    [Fact]
    public void The_output_with_a_bidi_control_in_the_symbol_equals_the_output_without_it()
    {
        var (trade, bars) = CardFixtures.BuyAtTarget();
        var (runA, dataA) = CardFixtures.Card(trade with { Symbol = "NSE:AB\u202EC" }, bars);
        var (runB, dataB) = CardFixtures.Card(trade with { Symbol = "NSE:ABC" }, bars);

        CardFixtures.Render(runA, dataA).ShouldBe(CardFixtures.Render(runB, dataB));
    }

    [Fact]
    public void The_aria_label_and_the_title_of_the_chart_hold_the_cleaned_ticker_and_never_the_raw_symbol()
    {
        var (run, data) = Hostile();

        var svg = CandleSvg.Render(data);

        svg.ShouldNotContain("<script");
        svg.ShouldContain("aria-label=\"_script_alert_1___script BUY 2026-03-03: ");
        run.Trades.Count.ShouldBe(1);
    }

    [Fact]
    public void Every_golden_card_passes_the_scan_and_the_scan_is_live()
    {
        foreach (var (name, run, data) in CardFixtures.Goldens())
        {
            CardScan.Problems(CardFixtures.Render(run, data), isIndex: false).ShouldBeEmpty(name);
        }

        var (_, run0, data0) = CardFixtures.Goldens()[0];
        var html = CardFixtures.Render(run0, data0);
        CardScan.Problems(html.Replace("<main>", "<main onclick=\"x\">", StringComparison.Ordinal), false).ShouldNotBeEmpty();
        CardScan.Problems(html.Replace("<main>", "<script>x</script><main>", StringComparison.Ordinal), false).ShouldNotBeEmpty();
        CardScan.Problems(html.Replace("<main>", "<a href=\"javascript:x\">y</a><main>", StringComparison.Ordinal), false).ShouldNotBeEmpty();
        CardScan.Problems(html.Replace("<main>", "<img src=\"http://x\"><main>", StringComparison.Ordinal), false).ShouldNotBeEmpty();
        CardScan.Problems(html.Replace("}</style>", "}p{background:url(x)}</style>", StringComparison.Ordinal), false).ShouldNotBeEmpty();
        CardScan.Problems(html.Replace("<meta http-equiv", "<meta data-x", StringComparison.Ordinal), false).ShouldNotBeEmpty();
    }

    [Fact]
    public void The_page_is_in_english_names_its_charset_and_policy_and_has_no_link_script_or_import()
    {
        var (_, run, data) = CardFixtures.Goldens()[0];

        var html = CardFixtures.Render(run, data);

        html.ShouldContain("<html lang=\"en\">");
        html.ShouldContain("<meta charset=\"utf-8\">");
        html.ShouldContain("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">");
        html.ShouldContain("<meta name=\"referrer\" content=\"no-referrer\">");
        html.ShouldContain("prefers-color-scheme:dark");
        foreach (var banned in new[] { "<script", "<link", "<img", "<iframe", "<object", "<embed", "@import", "url(", "xmlns" })
        {
            html.ShouldNotContain(banned);
        }
    }

    // no statistics

    private static IEnumerable<(string Name, string Html)> AllCards()
    {
        foreach (var (name, run, data) in CardFixtures.Goldens())
        {
            yield return (name, CardFixtures.Render(run, data));
        }

        var (costedRun, costedData) = CardFixtures.CostedSell();
        yield return ("costed", CardFixtures.Render(costedRun, costedData));
    }

    [Fact]
    public void No_text_of_a_card_holds_a_word_of_a_statistic_or_a_figure_in_r_with_a_digit_before_it_and_the_scan_is_live()
    {
        foreach (var (name, html) in AllCards())
        {
            var text = HtmlReader.VisibleText(HtmlReader.Read(html));

            ForbiddenWords.In(text).ShouldBeEmpty(name);
            ForbiddenWords.In(html).ShouldBeEmpty(name + " (the whole page, markup included)");
            text.ShouldNotContain("%\u0020of trades");
        }

        ForbiddenWords.In("the win rate is 2 R").ShouldBe(["win", "rate", "<digit> R"]);
    }

    [Fact]
    public void A_card_prints_only_the_numbers_of_its_own_trade_no_count_of_trades_and_no_percentage_of_them()
    {
        foreach (var (name, html) in AllCards())
        {
            var text = HtmlReader.VisibleText(HtmlReader.Read(html));

            text.ShouldNotContain("trades of the run", Case.Insensitive, name);
            html[html.IndexOf("</header>", StringComparison.Ordinal)..].ShouldNotContain("%", Case.Sensitive, name + ": no percentage outside the banner");
        }
    }
}
