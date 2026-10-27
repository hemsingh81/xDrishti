namespace XDrishti.Domain.Tests.Assistant.Evaluation;

public sealed class AnswerNumbersTests
{
    [Theory]
    [InlineData("It is 45.83% now", "45.83", 2, true)]
    [InlineData("It is ₹1,23,456.50 total", "123456.50", 2, false)]
    [InlineData("value Rs. 10,000 only", "10000", 0, false)]
    [InlineData("a loss of −1,000 rupees", "-1000", 0, false)]
    [InlineData("P&L is -₹1,000.", "-1000", 0, false)]
    [InlineData("P&L is ₹-1,000.", "-1000", 0, false)]
    [InlineData("gives 1.5R per trade", "1.5", 1, false)]
    [InlineData("profit factor 1.31x", "1.31", 2, false)]
    [InlineData("4.0 R", "4.0", 1, false)]
    public void Reads_one_figure_with_its_value_and_decimals(string text, string value, int decimals, bool percent)
    {
        var figure = AnswerNumbers.Find(text).ShouldHaveSingleItem();

        figure.Value.ShouldBe(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        figure.Decimals.ShouldBe(decimals);
        figure.IsPercent.ShouldBe(percent);
    }

    [Theory]
    [InlineData("the 2026-03-03 plan")]
    [InlineData("ORB 15m and EVH1 and ORB15")]
    [InlineData("at 09:15 IST")]
    [InlineData("in 2026")]
    [InlineData("see [1] and [2, 3] and call #4")]
    [InlineData("1. first item\n2) second item")]
    [InlineData("v1.2.3 build")]
    [InlineData("the 250d window and the 1st trade")]
    public void Does_not_read_dates_identifiers_markers_years_or_timeframes_as_figures(string text) =>
        AnswerNumbers.Find(text).ShouldBeEmpty();

    [Fact]
    public void Splits_sentences_at_line_breaks_and_after_a_full_stop_but_not_inside_a_decimal()
    {
        const string text = "Win rate is 45.83%. Total R is 4.0\nOK; done";

        var parts = AnswerNumbers.Sentences(text).Select(s => text[s.Start..s.End]).ToList();

        parts.ShouldBe(["Win rate is 45.83%.", "Total R is 4.0", "OK;", "done"]);
    }
}
