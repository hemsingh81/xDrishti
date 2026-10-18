using System.Text.RegularExpressions;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests.Cards;

/// <summary>Criteria 5, 6 and 7 of P3-04c, the index: the table, the relative links, the scan and the absence of counts by outcome.</summary>
public sealed partial class IndexHtmlTests
{
    [GeneratedRegex(@"^cards/[A-Za-z0-9._&-]+\.html$", RegexOptions.CultureInvariant)]
    private static partial Regex LinkPattern();

    private static string Render(SimulationRun run, int cap, IReadOnlyList<CardChoice> choices) =>
        Sinks.Render(s => IndexHtml.Write(s, run, CardFixtures.Labels(run), cap, choices));

    [Fact]
    public void The_index_of_the_standard_run_equals_the_golden_file_golden()
    {
        var run = FabricatedRun.Standard();

        Golden.CheckCard("index-standard", Render(run, 5, CardSelection.Choose(run, 5)));
    }

    [Fact]
    public void Every_card_has_a_row_with_its_number_symbol_side_date_exit_and_own_r_and_a_relative_link_that_matches_the_pattern()
    {
        var run = FabricatedRun.Standard();
        var choices = CardSelection.Choose(run, 5);

        var html = Render(run, 5, choices);

        var tokens = HtmlReader.Read(html);
        var links = tokens.OfType<HtmlToken.Start>().Where(t => t.Name == "a").Select(t => t.Attr("href")!).ToList();
        links.ShouldBe([.. choices.Select(c => CardNames.Of(c.Number, run.Trades.Count, c.Trade.Trade))]);
        links.ShouldAllBe(l => LinkPattern().IsMatch(l));
        var text = HtmlReader.VisibleText(tokens);
        text.ShouldContain("NSE:AAA");
        text.ShouldContain("NSE:M&M");
        text.ShouldContain("+2.000");
        text.ShouldContain("-1.000");
        CardScan.Problems(html, isIndex: true).ShouldBeEmpty();
        CardScan.Problems(html, isIndex: false).ShouldNotBeEmpty("a card may not have an href");
    }

    [Fact]
    public void A_run_with_no_trades_has_an_index_with_an_empty_table()
    {
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3))]);

        var html = Render(run, 200, CardSelection.Choose(run, 200));

        html.ShouldContain("cards asked for: up to 200; the run has 0 trades; cards written: 0");
        html.ShouldContain("<tbody>\n</tbody>");
        CardScan.Problems(html, isIndex: true).ShouldBeEmpty();
    }

    [Fact]
    public void The_index_shows_the_banner_the_cap_and_the_number_of_trades_but_no_size_of_a_stratum_and_no_statistic()
    {
        var run = FabricatedRun.Standard();

        var html = Render(run, 5, CardSelection.Choose(run, 5));

        html.ShouldContain("cards asked for: up to 5; the run has 2 trades; cards written: 2");
        html.ShouldContain("exploration: signal-level mechanics");
        ForbiddenWords.In(html).ShouldBeEmpty();
        ForbiddenWords.In(HtmlReader.VisibleText(HtmlReader.Read(html))).ShouldBeEmpty();
        foreach (var stratum in CardSelection.StratumNames)
        {
            html.ShouldNotContain(stratum);
        }
    }

    [Fact]
    public void A_hostile_symbol_is_escaped_in_the_table_and_the_link_is_the_cleaned_name()
    {
        var hostile = "NSE:<script>alert(1)</script>\"'&\u202E";
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, hostile, Side.Buy, new DateOnly(2026, 3, 2));
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, hostile)), [FabricatedRun.Ran(FabricatedRun.IdA, hostile, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3))], [trade]);

        var html = Render(run, 5, CardSelection.Choose(run, 5));

        html.ShouldNotContain("<script");
        html.ShouldNotContain('\u202E');
        CardScan.Problems(html, isIndex: true).ShouldBeEmpty();
        HtmlReader.Read(html).OfType<HtmlToken.Start>().Single(t => t.Name == "a").Attr("href").ShouldBe("cards/0001-_script_alert_1___script-buy-2026-03-02-target.html");
    }
}
