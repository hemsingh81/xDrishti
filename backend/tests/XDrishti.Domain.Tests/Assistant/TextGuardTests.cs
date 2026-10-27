using XDrishti.Domain.Assistant.Grounding;
using XDrishti.Domain.Assistant.Text;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec P9-06 AC 2 to 7: the deterministic text passes the guard (its oracle) and the guard rejects what it cannot verify against the facts.</summary>
public sealed class TextGuardTests
{
    private static readonly BriefingFacts Facts = BriefingFixtures.Daily();

    private const string Good = """
        Plan for 2026-10-28 is published with 2 primary and 2 reserve tickets and it is provisional.
        - AAA BUY primary ticket: stop 98.50, target 104.00, pWin 58.3%.
        - BBB SELL primary ticket: stop 205.00, target 196.00, pWin 55.1%.
        - CCC BUY reserve ticket: stop 49.00, target 53.00.
        - DDD SELL reserve ticket: stop 77.00, target 72.00.
        Regime is unavailable.
        """;

    private static List<string> Codes(string text, BriefingFacts? facts = null) => TextGuard.Check(text, facts ?? Facts, 300).Findings.Select(f => f.Code).Distinct().ToList();

    [Fact]
    public void A_faithful_text_passes()
    {
        var result = TextGuard.Check(Good, Facts, 300);

        result.Findings.ShouldBeEmpty();
        result.Passed.ShouldBeTrue();
    }

    [Fact]
    public void The_deterministic_briefing_passes_the_guard_and_is_identical_on_repeat()
    {
        var first = DeterministicBriefing.Build(Facts);
        var again = DeterministicBriefing.Build(BriefingFixtures.Daily());

        again.ShouldBe(first);
        TextGuard.Check(first, Facts, 300).Findings.ShouldBeEmpty();
        TextGuard.WordCount(first).ShouldBeLessThanOrEqualTo(300);
        first.ShouldEndWith(BriefingWording.DailyFooter);
        first.IndexOf("AAA BUY", StringComparison.Ordinal).ShouldBeLessThan(first.IndexOf("BBB SELL", StringComparison.Ordinal));
        first.IndexOf("BBB SELL", StringComparison.Ordinal).ShouldBeLessThan(first.IndexOf("CCC BUY", StringComparison.Ordinal));
        first.IndexOf("CCC BUY", StringComparison.Ordinal).ShouldBeLessThan(first.IndexOf("DDD SELL", StringComparison.Ordinal));
    }

    [Fact]
    public void Replacing_any_single_figure_of_the_deterministic_text_by_a_value_not_in_the_facts_is_refused()
    {
        var text = DeterministicBriefing.Build(Facts);
        var figures = NumberTokenizer.Figures(text);
        figures.Count.ShouldBeGreaterThan(20);

        foreach (var figure in figures)
        {
            var doctored = text[..figure.Start] + "987654.3" + text[figure.End..];

            TextGuard.Check(doctored, Facts, 300).Findings.ShouldContain(f => f.Code == GuardCodes.NumberNotInFacts && f.Token == "987654.3", $"figure {figure.Text} at {figure.Start}");
        }
    }

    [Theory]
    [InlineData("AAA BUY ticket has stop 98.60.")]
    [InlineData("AAA BUY ticket has stop 98.5000.")]
    [InlineData("AAA BUY ticket has 62 trades.")]
    [InlineData("AAA BUY ticket has pWin 58.4%.")]
    [InlineData("AAA BUY ticket has pWin 58.3.")]
    public void An_invented_or_over_precise_figure_is_refused(string sentence) =>
        TextGuard.Check(Good + "\n" + sentence, Facts, 400).Findings.ShouldContain(f => f.Code == GuardCodes.NumberNotInFacts);

    [Theory]
    [InlineData("45.8")]
    [InlineData("46")]
    [InlineData("45.83")]
    public void Rounding_half_away_from_zero_to_fewer_decimals_is_allowed(string token) =>
        Codes(Good + $"\nThe reference value is {token}.").ShouldBeEmpty();

    [Theory]
    [InlineData("45.9")]
    [InlineData("45.834")]
    [InlineData("47")]
    public void Any_other_reading_of_a_figure_is_refused(string token) =>
        Codes(Good + $"\nThe reference value is {token}.").ShouldContain(GuardCodes.NumberNotInFacts);

    [Fact]
    public void No_arithmetic_is_allowed_a_sum_is_not_in_the_facts() =>
        Codes(Good + "\nThe two primary tickets have 131 trades together.").ShouldContain(GuardCodes.NumberNotInFacts);

    [Fact]
    public void The_sign_matters_a_loss_is_not_a_gain()
    {
        Codes(Good + "\nAAA lost −0.30R last week.").ShouldBeEmpty();
        Codes(Good + "\nAAA lost -0.30R last week.").ShouldBeEmpty();
        Codes(Good + "\nAAA made 0.30R last week.").ShouldContain(GuardCodes.NumberNotInFacts);
    }

    [Fact]
    public void A_figure_in_the_sentence_of_another_ticket_is_a_scope_error()
    {
        var codes = TextGuard.Check(Good + "\nAAA BUY ticket has its stop at 205.00.", Facts, 400).Findings;

        codes.ShouldContain(f => f.Code == GuardCodes.NumberWrongScope && f.Token == "205.00");
    }

    [Theory]
    [InlineData("AAA BUY ticket has pnl1250.")]
    [InlineData("AAA BUY ticket has net-45 today.")]
    [InlineData("AAA BUY ticket closed at 1950.")]
    [InlineData("The stop is 98.50.")]
    public void Digits_hidden_from_the_tokenizer_and_scoped_figures_in_the_wrong_sentence_are_refused(string sentence) => Codes(Good + "\n" + sentence).ShouldNotBeEmpty();

    [Fact]
    public void A_figure_of_the_facts_in_a_sentence_naming_no_ticket_is_allowed() =>
        Codes(Good + "\nThe gate cut the risk to 0.50 of normal.").ShouldBeEmpty();

    [Theory]
    [InlineData("The briefing is dated 2026-10-29.")]
    [InlineData("The plan opens at 09:15.")]
    [InlineData("The plan was made on 29 Oct.")]
    public void A_date_or_time_that_is_not_in_the_facts_is_refused(string sentence) => Codes(Good + "\n" + sentence).ShouldContain(GuardCodes.NumberNotInFacts);

    [Fact]
    public void A_number_word_needs_the_same_word_in_the_facts() =>
        Codes(Good + "\nThere are two reserve tickets.").ShouldContain(GuardCodes.NumberWord);

    [Fact]
    public void A_symbol_that_is_not_in_the_facts_is_refused_but_an_allowed_acronym_is_not()
    {
        Codes(Good + "\nZZZ also moved.").ShouldContain(GuardCodes.SymbolNotInFacts);
        Codes(Good + "\nThe ATR and the VWAP are not used.").ShouldBeEmpty();
    }

    [Fact]
    public void Every_planned_ticket_must_be_named()
    {
        var text = Good.Replace("- DDD SELL reserve ticket: stop 77.00, target 72.00.\n", string.Empty, StringComparison.Ordinal);

        TextGuard.Check(text, Facts, 300).Findings.ShouldContain(f => f.Code == GuardCodes.TicketNotCovered && f.Token == "DDD");
    }

    [Theory]
    [InlineData("**Bold** words.", GuardCodes.FormMarkdown)]
    [InlineData("# A heading", GuardCodes.FormMarkdown)]
    [InlineData("See https://example.invalid/x now.", GuardCodes.FormUrl)]
    [InlineData("Some `code` here.", GuardCodes.FormCode)]
    [InlineData("Buy AAA at once.", GuardCodes.FormImperative)]
    [InlineData("- Hold BBB overnight.", GuardCodes.FormImperative)]
    [InlineData("AAA will rise tomorrow.", GuardCodes.FormForecast)]
    [InlineData("AAA is expected to gain.", GuardCodes.FormForecast)]
    [InlineData("AAA is a sure thing.", GuardCodes.FormForecast)]
    public void The_form_rules_refuse_markup_advice_and_forecasts(string sentence, string code) => Codes(Good + "\n" + sentence).ShouldContain(code);

    [Fact]
    public void Too_many_words_is_refused_and_the_limit_is_the_callers() =>
        TextGuard.Check(Good, Facts, 20).Findings.ShouldContain(f => f.Code == GuardCodes.FormTooLong);

    [Fact]
    public void A_fact_marked_unavailable_may_not_carry_a_figure() =>
        Codes(Good + "\nThe regime is 3 today.").ShouldContain(GuardCodes.HonestyFigureForUnavailable);

    [Fact]
    public void A_provisional_plan_must_say_so()
    {
        Codes(Good.Replace("and it is provisional", string.Empty, StringComparison.Ordinal)).ShouldContain(GuardCodes.HonestyProvisionalMissing);
        Codes(Good.Replace("provisional", "not paper-proven", StringComparison.Ordinal)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("This is risk-free.")]
    [InlineData("Profit guaranteed here.")]
    [InlineData("We are certain of it.")]
    public void Forbidden_claims_are_refused(string sentence) => Codes(Good + "\n" + sentence).ShouldContain(GuardCodes.HonestyNeverSay);

    [Fact]
    public void An_empty_text_is_refused() => TextGuard.Check("  ", Facts, 300).Findings.ShouldHaveSingleItem().Code.ShouldBe(GuardCodes.Empty);

    [Fact]
    public void The_published_word_lists_are_pinned()
    {
        GuardRules.ImperativeVerbs.ShouldBe(["buy", "sell", "short", "enter", "exit", "hold", "add", "average"]);
        BriefingWording.AllowedAcronyms.ShouldBe(["NSE", "IST", "ATR", "VIX", "ORB", "VWAP", "EMA", "RSI", "SL", "BUY", "SELL", "INTRADAY", "SWING"]);
        BriefingWording.NeverSay.ShouldBe(["profit guaranteed", "risk-free", "certain"]);
        GuardRules.NumberWords.ShouldContain("lakh");
    }

    [Fact]
    public void With_no_ticket_the_deterministic_text_says_so_and_lists_the_basis()
    {
        var facts = new BriefingFacts(BriefingFacts.Daily, "2026-10-28", false, [], [
            new FactLine(string.Empty, "plan", "plan", "Plan for 2026-10-28 is published with 0 primary and 0 reserve tickets; 4 candidates were rejected."),
            new FactLine(string.Empty, "plan", "tickets", "No ticket is planned for this date."),
            new FactLine(string.Empty, "risk", "risk", "Gate no_trade: the market gate allows no new ticket."),
        ], 1, null, []);

        var text = DeterministicBriefing.Build(facts);

        text.ShouldContain("No ticket is planned for this date.");
        text.ShouldContain("Gate no_trade");
        TextGuard.Check(text, facts, 300).Findings.ShouldBeEmpty();
    }

    [Fact]
    public void A_long_fact_set_is_cut_to_the_word_limit_without_breaking_the_guard()
    {
        var lines = Facts.Lines.Concat(Enumerable.Range(0, 30).Select(i => new FactLine(string.Empty, "risk", "risk", $"Risk line {i} says that the data is thin and the plan is provisional for a long list of reasons that go on."))).ToList();
        var facts = Facts with { Lines = lines };

        var text = DeterministicBriefing.Build(facts);

        TextGuard.WordCount(text).ShouldBeLessThanOrEqualTo(300);
        TextGuard.Check(text, facts, 300).Findings.ShouldBeEmpty();
    }
}
