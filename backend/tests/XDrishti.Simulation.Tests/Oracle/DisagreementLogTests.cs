using System.Globalization;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// Acceptance criterion 10 of P3-08 (D9): the disagreement log <c>docs/research/p3-08-disagreements.md</c> has the thirteen fixed columns; every entry has a class (A to D) and a verdict, a class B or C entry has a regression row, no entry is
/// open; and the log lists the number of series compared per generator, which the run confirms (the numbers in the file are the numbers of this suite, so an edit of a generator count that is not written down fails here).
/// </summary>
public sealed class DisagreementLogTests
{
    private static readonly string[] Classes = ["A", "B", "C", "D"];

    private static readonly string[] Columns = ["id", "generator", "seed", "category", "field", "oracle", "engine", "rule row", "minimal series", "class", "verdict (ADR sentence)", "fix", "regression row"];

    internal sealed record Entry(string Id, string Class, string Verdict, string Regression, string Generator);

    private static string[] Lines()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Oracle", "Docs", "p3-08-disagreements.md");
        return File.ReadAllLines(path);
    }

    private static List<string[]> TableRows(IEnumerable<string> lines, string header)
    {
        var rows = new List<string[]>();
        var inTable = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("| " + header, StringComparison.Ordinal))
            {
                inTable = true;
                continue;
            }

            if (!inTable)
            {
                continue;
            }

            if (!line.StartsWith('|'))
            {
                break;
            }

            if (line.StartsWith("|---", StringComparison.Ordinal))
            {
                continue;
            }

            rows.Add(line.Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries));
        }

        return rows;
    }

    internal static List<Entry> Entries() =>
        [.. TableRows(Lines(), "id | generator").Where(r => r.Length == Columns.Length).Select(r => new Entry(r[0], r[9], r[10], r[12], r[1]))];

    [Fact]
    public void The_log_has_the_thirteen_columns_in_the_fixed_order()
    {
        var header = Lines().First(l => l.StartsWith("| id | generator", StringComparison.Ordinal)).Trim().Trim('|').Split('|', StringSplitOptions.TrimEntries);

        header.ShouldBe(Columns);
    }

    [Fact]
    public void Every_entry_has_a_class_a_verdict_and_for_a_class_B_or_C_entry_a_regression_row()
    {
        var entries = Entries();

        entries.ShouldNotBeEmpty();
        foreach (var entry in entries)
        {
            Classes.ShouldContain(entry.Class, $"{entry.Id}: class");
            entry.Verdict.Length.ShouldBeGreaterThan(20, $"{entry.Id}: verdict");
            entry.Verdict.ShouldNotContain("OPEN", Case.Insensitive, $"{entry.Id}: an open entry blocks P3-09");
            if (entry.Class is "B" or "C")
            {
                entry.Regression.ShouldNotBeNullOrWhiteSpace($"{entry.Id}: a class {entry.Class} entry needs its permanent hand row");
                entry.Regression.ShouldNotBe("-", $"{entry.Id}: a class {entry.Class} entry needs its permanent hand row");
            }
        }

        entries.Select(e => e.Id).ShouldBeUnique();
    }

    [Fact]
    public void The_sentinels_an_entry_without_a_class_or_a_regression_row_is_refused()
    {
        // the rule above, applied to doctored entries
        static bool Acceptable(Entry e) => Classes.Contains(e.Class) && e.Verdict.Length > 20 && !(e.Class is "B" or "C" && (string.IsNullOrWhiteSpace(e.Regression) || e.Regression == "-"));

        Acceptable(new Entry("x", "A", "a verdict that cites an ADR sentence", "-", "g")).ShouldBeTrue();
        Acceptable(new Entry("x", "", "a verdict that cites an ADR sentence", "-", "g")).ShouldBeFalse();
        Acceptable(new Entry("x", "A", "", "-", "g")).ShouldBeFalse();
        Acceptable(new Entry("x", "B", "a verdict that cites an ADR sentence", "-", "g")).ShouldBeFalse();
        Acceptable(new Entry("x", "C", "a verdict that cites an ADR sentence", "", "g")).ShouldBeFalse();
    }

    [Fact]
    public void The_log_lists_the_series_compared_per_generator_and_the_numbers_are_the_runs()
    {
        var rows = TableRows(Lines(), "generator | series").ToDictionary(r => r[0], r => r);
        foreach (var tally in Corpus.All)
        {
            rows.ContainsKey(tally.Name).ShouldBeTrue($"the log has no row for the generator '{tally.Name}'");
            var row = rows[tally.Name];
            row[1].ShouldBe(tally.Series.ToString(CultureInfo.InvariantCulture), $"{tally.Name}: series");
            row[2].ShouldBe(tally.Comparisons.ToString(CultureInfo.InvariantCulture), $"{tally.Name}: comparisons");
            row[3].ShouldBe(tally.Candidates.ToString(CultureInfo.InvariantCulture), $"{tally.Name}: candidates");
            row[4].ShouldBe(tally.Trades.ToString(CultureInfo.InvariantCulture), $"{tally.Name}: trades");
        }

        rows.Count.ShouldBe(Corpus.All.Count, "the log lists exactly the generators of the run");
    }
}
