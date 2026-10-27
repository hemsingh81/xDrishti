using XDrishti.Domain.Assistant.Text;

namespace XDrishti.Domain.Tests.Assistant.Evaluation;

/// <summary>Spec P9-09 AC 5: raw, repaired, escape and covered; the independent audit and the guard agree on 1000 generated texts.</summary>
public sealed class PlanGradingTests
{
    private static readonly BriefingFacts Facts = BriefingFixtures.Daily();

    private static readonly PlanCase Plan = new("PL-T", "test", Facts, Facts.Tickets, ["provisional"]);

    private const string Good = """
        Plan for 2026-10-28 is published with 2 primary and 2 reserve tickets and it is provisional.
        - AAA BUY primary ticket: stop 98.50, target 104.00, pWin 58.3%.
        - BBB SELL primary ticket: stop 205.00, target 196.00, pWin 55.1%.
        - CCC BUY reserve ticket: stop 49.00, target 53.00.
        - DDD SELL reserve ticket: stop 77.00, target 72.00.
        Regime is unavailable.
        """;

    private const string BadNumber = Good + "\nAAA expectancy is 0.99R.";

    [Fact]
    public void A_faithful_first_text_is_raw_repaired_covered_and_does_not_escape()
    {
        var grade = BriefingGrader.Grade(Plan, [Good], 300);

        (grade.RawPass, grade.RepairedPass, grade.Escape, grade.Covered).ShouldBe((true, true, false, true));
        grade.Passed.ShouldBeTrue();
        grade.Findings.ShouldBeEmpty();
    }

    [Fact]
    public void A_refused_first_text_and_an_accepted_second_one_is_repaired_but_not_raw()
    {
        var grade = BriefingGrader.Grade(Plan, [BadNumber, Good], 300);

        (grade.RawPass, grade.RepairedPass, grade.Escape, grade.Covered).ShouldBe((false, true, false, true));
        grade.Findings.ShouldContain(GuardCodes.NumberNotInFacts);
        grade.Passed.ShouldBeTrue();
    }

    [Fact]
    public void Two_refused_texts_are_neither_raw_nor_repaired_and_the_case_does_not_pass()
    {
        var grade = BriefingGrader.Grade(Plan, [BadNumber, BadNumber], 300);

        (grade.RawPass, grade.RepairedPass).ShouldBe((false, false));
        grade.Passed.ShouldBeFalse();
    }

    [Fact]
    public void A_text_that_misses_a_symbol_is_not_covered()
    {
        var grade = BriefingGrader.Grade(Plan, [Good.Replace("DDD", "the last one", StringComparison.Ordinal)], 300);

        grade.Covered.ShouldBeFalse();
        grade.Passed.ShouldBeFalse();
    }

    [Fact]
    public void No_attempt_at_all_passes_nothing()
    {
        var grade = BriefingGrader.Grade(Plan, [], 300);

        (grade.RawPass, grade.RepairedPass, grade.Covered, grade.Passed).ShouldBe((false, false, false, false));
    }

    [Theory]
    [InlineData("Plan for 2026-10-28 shows 0.99R.", 1)]
    [InlineData("AAA stop 98.50 and AAA stop 98.5.", 0)]
    [InlineData("AAA stop 98.", 1)]
    [InlineData("AAA pWin 58% on 61 trades.", 0)]
    [InlineData("AAA pWin 59.1% on 61 trades.", 1)]
    [InlineData("It was 15m and EV12 and ORB15 in 2019.", 1)]
    [InlineData("At 09:15 on 2026-10-28 ORB15.", 0)]
    [InlineData("A net of 2,49,93,750 rupees.", 1)]
    public void The_audit_lists_the_figures_that_no_fact_holds(string text, int absent) =>
        IndependentNumberAudit.Absent(text, Facts).Count.ShouldBe(absent);

    [Fact]
    public void A_rounded_figure_is_in_the_facts_and_a_truncated_one_is_not()
    {
        IndependentNumberAudit.Absent("AAA stop 99 target 104.", Facts).ShouldBe([]);
        IndependentNumberAudit.Absent("AAA stop 98.4.", Facts).ShouldBe(["98.4"]);
    }

    [Fact]
    public void The_independent_audit_and_the_guard_agree_on_a_thousand_generated_texts()
    {
        var random = new Random(20261027);
        // Plain figures only (the guard also tells a percent from a plain figure, the audit asks only whether the number is in the facts at all).
        string[] originals = ["98.50", "104.00", "205.00", "196.00", "49.00", "53.00", "77.00", "72.00", "0.34", "0.21", "61", "70"];
        string[] roundings = ["99", "104", "205", "196", "49", "53", "77", "72", "0.3", "0.2", "61", "70"];
        string[] novel = ["317.77", "913.4", "4821", "88.9", "6.61", "7321.4", "0.77", "1250.25", "34", "21.3"];
        var disagreements = new List<string>();
        var refused = 0;
        for (var n = 0; n < 1000; n++)
        {
            var parts = new List<string> { "Plan for 2026-10-28 is published with 2 primary and 2 reserve tickets and it is provisional." };
            foreach (var (symbol, side) in new[] { ("AAA", "BUY"), ("BBB", "SELL"), ("CCC", "BUY"), ("DDD", "SELL") })
            {
                var a = random.Next(originals.Length);
                var first = random.Next(5) switch { 0 or 1 => originals[a], 2 or 3 => roundings[a], _ => novel[random.Next(novel.Length)] };
                var second = random.Next(8) == 0 ? novel[random.Next(novel.Length)] : originals[random.Next(originals.Length)];
                parts.Add($"- {symbol} {side} ticket: stop {first}, target {second}.");
            }

            parts.Add("Regime is unavailable.");
            var text = string.Join('\n', parts);

            var guardSays = TextGuard.Check(text, Facts, 300).Findings.Any(f => f.Code == GuardCodes.NumberNotInFacts);
            var auditSays = IndependentNumberAudit.Absent(text, Facts).Count > 0;
            refused += guardSays ? 1 : 0;
            if (guardSays != auditSays)
            {
                disagreements.Add($"guard {guardSays}, audit {auditSays}: {text}");
            }
        }

        disagreements.ShouldBeEmpty(string.Join('\n', disagreements.Take(3)));
        refused.ShouldBeInRange(200, 900, "the generator makes both kinds of text");
    }
}
