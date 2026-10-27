using XDrishti.Domain.Assistant.Grounding;

namespace XDrishti.Domain.Tests.Assistant;

/// <summary>Spec P9-03 AC 6: the grounding check. Hand cases (a) to (i), the properties (the text is never changed, the spans are inside the text and do not overlap) and the negative control (10 doctored answers all flagged, 30 undoctored none).</summary>
public sealed class GroundingCheckTests
{
    private const string Report = """{"ok":true,"tool":"get_report","asOf":"2026-10-27T09:15:00+05:30","rows":[{"expectancy":0.1187,"n":143,"winRate":0.4126,"lost":-0.3,"net":123456.5,"cap":1200000,"profitFactor":1.84,"drawdown":-12.5}],"evidence":{"basis":"gross","n":143,"dateRange":"2025-01-01..2025-12-31"}}""";

    private static GroundingResult Check(string answer, string question = "How is ORB15 doing?", string arguments = """{"report":"summary","query":{"runs":"sim-1"}}""", params string[] results) =>
        GroundingCheck.Check(answer, question, [arguments], results.Length == 0 ? [Report] : results);

    [Fact]
    public void A_rounded_answer_with_every_figure_in_the_result_is_grounded()
    {
        var result = Check("Expectancy is +0.12R over 143 trades, win rate 41.3 % [1].");

        result.Status.ShouldBe(GroundingStatus.Grounded);
        result.Checked.ShouldBe(3);
        result.Ungrounded.ShouldBeEmpty();
    }

    [Fact]
    public void A_count_that_is_not_the_count_is_ungrounded_with_one_span()
    {
        var answer = "There were about 46 trades.";

        var result = Check(answer);

        result.Status.ShouldBe(GroundingStatus.Ungrounded);
        var span = result.Ungrounded.ShouldHaveSingleItem();
        answer.Substring(span.Start, span.End - span.Start).ShouldBe("46");
        span.Text.ShouldBe("46");
    }

    [Fact]
    public void An_invented_seven_digit_figure_is_flagged() => Check("Net profit was ₹9,876,543.").Ungrounded.ShouldHaveSingleItem();

    [Theory]
    [InlineData("On 2026-10-27 at 09:15 the 15m setups of ORB15 on NIFTY50 and 1D bars over 250d; see [1] and [2, 3].")]
    [InlineData("1. First point\n2) second point, since 12 March 2025 and Mar 5, 2024, version v10.0.13, FY2026-27, call 2 and #3.")]
    [InlineData("In 2025 it ran R2 and Q3 tests.")]
    public void Dates_times_list_numbers_markers_timeframes_identifiers_and_years_are_not_figures(string answer)
    {
        NumberTokenizer.Figures(answer).ShouldBeEmpty();
        Check(answer, "q", "{}", "{}").Status.ShouldBe(GroundingStatus.NoNumbers);
    }

    [Fact]
    public void A_year_followed_by_a_count_noun_is_a_figure() => NumberTokenizer.Figures("2025 trades").Count.ShouldBe(1);

    [Fact]
    public void A_figure_in_the_question_or_in_an_argument_passes()
    {
        Check("You asked about 77 trades.", question: "Why 77 trades?", results: "{}").Status.ShouldBe(GroundingStatus.Grounded);
        Check("I used 12 runs.", arguments: """{"limit":12}""", results: "{}").Status.ShouldBe(GroundingStatus.Grounded);
    }

    [Fact]
    public void A_difference_the_model_computed_is_flagged_and_the_same_difference_from_calc_passes()
    {
        string[] results = [Report, """{"ok":true,"tool":"calc","data":{"value":"0.0261","places":4}}"""];

        Check("The gap is 0.026 [2].", results: Report).Status.ShouldBe(GroundingStatus.Ungrounded);
        Check("The gap is 0.026 [2].", results: results).Status.ShouldBe(GroundingStatus.Grounded);
    }

    [Theory]
    [InlineData("-0.3R", true)]
    [InlineData("lost 0.3R", true)]
    [InlineData("a drawdown of 12.5 %", true)]
    [InlineData("a drawdown of -12.5%", true)]
    [InlineData("−0.3R", true)]
    [InlineData("+0.7R", false)]
    public void Sign_percent_and_unit_rules(string answer, bool grounded) =>
        Check(answer).Status.ShouldBe(grounded ? GroundingStatus.Grounded : GroundingStatus.Ungrounded);

    [Theory]
    [InlineData("41 %", true)]
    [InlineData("41.3%", true)]
    [InlineData("41.26 percent", true)]
    [InlineData("0.45", false)]
    [InlineData("43%", false)]
    public void A_percent_matches_the_ratio_times_one_hundred(string answer, bool grounded) =>
        Check(answer).Status.ShouldBe(grounded ? GroundingStatus.Grounded : GroundingStatus.Ungrounded);

    [Theory]
    [InlineData("₹1,23,456.50", true)]
    [InlineData("Rs 1,23,456.5", true)]
    [InlineData("Rs.123456", true)]
    [InlineData("₹1.23L", true)]
    [InlineData("₹1.2 lakh", true)]
    [InlineData("123k", true)]
    [InlineData("₹0.12 Cr", true)]
    [InlineData("₹12,00,000", true)]
    [InlineData("₹13,00,000", false)]
    [InlineData("₹2 Cr", false)]
    public void Indian_grouping_currency_and_units_parse(string answer, bool grounded) =>
        Check(answer).Status.ShouldBe(grounded ? GroundingStatus.Grounded : GroundingStatus.Ungrounded);

    [Theory]
    [InlineData("0.125", 0.125, "0.13", true)]
    [InlineData("0.125", 0.125, "0.12", true)]
    [InlineData("0.125", 0.125, "0.14", false)]
    [InlineData("1e-3", 0.001, "0.001", true)]
    [InlineData("1e-3", 0.001, "1e-3", true)]
    public void Rounding_boundaries_are_inclusive_and_exponents_parse(string sourceText, double _, string figure, bool grounded) =>
        GroundingCheck.Check($"It is {figure}.", "q", ["{}"], [$$"""{"v":{{sourceText}}}"""]).Status.ShouldBe(grounded ? GroundingStatus.Grounded : GroundingStatus.Ungrounded);

    [Fact]
    public void A_number_glued_to_text_is_not_a_figure_and_a_negative_zero_is_zero()
    {
        NumberTokenizer.Figures("R2 and v10.0.13 and ORB15").ShouldBeEmpty();
        Check("it was -0.0", results: """{"v":0}""").Status.ShouldBe(GroundingStatus.Grounded);
    }

    [Fact]
    public void An_answer_with_no_tool_result_and_a_figure_not_in_the_question_is_ungrounded()
    {
        var result = GroundingCheck.Check("Expectancy is 0.12R.", "How is it?", [], []);

        result.Status.ShouldBe(GroundingStatus.Ungrounded);
    }

    [Fact]
    public void Json_lists_of_small_integers_are_sources_both_grouped_and_split() =>
        Check("It has 1,234 and 234 and 1.", results: """{"v":[1,234]}""").Status.ShouldBe(GroundingStatus.Grounded);

    [Fact]
    public void The_text_is_never_changed_and_the_spans_are_inside_it_and_do_not_overlap()
    {
        const string answer = "Win rate 41.3 %, expectancy +0.12R, n 143, a made-up 99,999 and 7.5k, ₹3 Cr, 5-6, 10-20 trades.";
        var copy = new string(answer.AsSpan());

        var result = Check(answer);

        answer.ShouldBe(copy);
        var last = 0;
        foreach (var span in result.Ungrounded)
        {
            span.Start.ShouldBeGreaterThanOrEqualTo(last);
            span.End.ShouldBeGreaterThan(span.Start);
            span.End.ShouldBeLessThanOrEqualTo(answer.Length);
            answer.Substring(span.Start, span.End - span.Start).ShouldBe(span.Text);
            last = span.End;
        }

        result.Ungrounded.ShouldNotBeEmpty();
    }

    [Fact]
    public void A_figure_that_does_not_fit_a_decimal_is_flagged() => Check("It is 123456789012345678901234567890123 big.").Ungrounded.ShouldHaveSingleItem();

    [Fact]
    public void Not_checked_has_no_spans() => (GroundingResult.NotChecked.Status, GroundingResult.NotChecked.Ungrounded.Count).ShouldBe((GroundingStatus.NotChecked, 0));

    [Fact]
    public void Status_codes_are_the_stored_words() =>
        new[] { GroundingStatus.Grounded, GroundingStatus.Ungrounded, GroundingStatus.NoNumbers, GroundingStatus.NotChecked }.Select(s => s.ToCode()).ShouldBe(["grounded", "ungrounded", "no_numbers", "not_checked"]);

    // 30 undoctored answers that quote the result in different ways; for each, the same answer with ONE figure changed to a value that is in no source.
    private static readonly (string Good, string Doctored)[] Controls =
    [
        ("Expectancy is 0.1187R over 143 trades.", "Expectancy is 0.1187R over 150 trades."),
        ("Win rate is 41.26 %, n 143.", "Win rate is 47.26 %, n 143."),
        ("Net was ₹1,23,456.50 [1].", "Net was ₹1,23,756.50 [1]."),
        ("Cap 12,00,000 and profit factor 1.84.", "Cap 12,00,000 and profit factor 2.84."),
        ("Drawdown 12.5% on a gross basis.", "Drawdown 13.5% on a gross basis."),
        ("Loss of 0.3R per losing trade.", "Loss of 0.7R per losing trade."),
        ("Basis gross, n=143.", "Basis gross, n=144."),
        ("Expectancy 0.12R, profit factor 1.8.", "Expectancy 0.32R, profit factor 1.8."),
        ("Net ₹1.23 L over 143 trades.", "Net ₹1.93 L over 143 trades."),
        ("About 41% won.", "About 61% won."),
    ];

    [Fact]
    public void Negative_control_ten_doctored_answers_are_all_flagged_and_thirty_undoctored_are_not()
    {
        var undoctored = Controls.Select(c => c.Good).Concat(
        [
            "Expectancy is +0.12R [1].", "Win rate 41.3 %.", "There are 143 trades [1].", "Net ₹123,456.5.", "Profit factor 1.84.", "Drawdown -12.5%.", "n is 143 and the win rate 0.4126.", "Losses average 0.3R.",
            "Net is Rs 1,23,456.50.", "Cap ₹12 L.", "Cap ₹12L.", "Win rate 41 %.", "Expectancy 0.119.", "Profit factor 1.84 over 143 trades.", "Cap 1200000.", "Drawdown 12.5 % over 143 trades.", "Net 123k.", "Net 123.5k.",
            "No figures here at all.", "Between 2025-01-01 and 2025-12-31 on ORB15.",
        ]).ToList();
        undoctored.Count.ShouldBe(30);

        foreach (var answer in undoctored)
        {
            Check(answer).Status.ShouldNotBe(GroundingStatus.Ungrounded, answer);
        }

        foreach (var (_, doctored) in Controls)
        {
            Check(doctored).Status.ShouldBe(GroundingStatus.Ungrounded, doctored);
        }
    }
}
