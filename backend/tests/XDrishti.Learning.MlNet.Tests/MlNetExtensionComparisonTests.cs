using System.Globalization;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Learning.MlNet.Tests;

/// <summary>P13-05, the one ML.NET check: the planted-effect fixture with the real recipe gives the same report and record id twice (the recipe's determinism is what criterion 2 relies on).</summary>
public sealed class MlNetExtensionComparisonTests
{
    private static readonly DateOnly From = new(2026, 1, 1);

    private static readonly DateOnly To = new(2026, 9, 30);

    private static readonly ExtensionGroup Group = new("md", "md-1", ["e1"]);

    private static List<ValidationRow> Planted()
    {
        var random = new SeededRandom(31);
        var rows = new List<ValidationRow>();
        var index = 0;
        for (var day = From; day <= To; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            for (var i = 0; i < 14; i++)
            {
                var win = random.Next(1000) < 500;
                var decision = new DateTimeOffset(day.Year, day.Month, day.Day, 4, i, 0, TimeSpan.Zero).ToUnixTimeSeconds();
                rows.Add(new ValidationRow("t" + index++.ToString("D6", CultureInfo.InvariantCulture), win ? 1 : 0, win ? 1.0 : -1.0, day, decision, decision + 1800, "S|15m|BUY|intraday|X|*", [random.Next(1000) / 1000.0, (win ? 1.0 : 0.0) + (random.Next(1000) / 10000.0)]));
            }
        }

        return rows;
    }

    [Fact]
    public void The_planted_effect_gives_the_same_verdict_report_and_record_id_twice_with_the_real_recipe()
    {
        var rows = Planted();
        var sessions = Enumerable.Range(0, (To.DayNumber - From.DayNumber) + 10).Select(i => From.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        var options = ExtensionComparisonOptions.ForDepth() with { RecipeOptionsHash = "defaults", LabelRuleVersion = "tb-1", Draws = 500 };
        var recipe = new MlNetScorerFactory();
        var ledger = new[] { ExtensionTrial.Of(Group, recipe.Name, options, From, To).Hash };

        var first = ExtensionComparison.Compare(rows, 1, Group, recipe, ledger, sessions, options).Value;
        var second = ExtensionComparison.Compare(rows, 1, Group, new MlNetScorerFactory(), ledger, sessions, options).Value;

        first.Verdict.ShouldBe(second.Verdict);
        first.FoldsEvaluable.ShouldBeGreaterThanOrEqualTo(6);
        var a = first.ToRecord("data");
        var b = second.ToRecord("data");
        (b.EvaluationId, b.EstimatesJson, b.EvidenceJson).ShouldBe((a.EvaluationId, a.EstimatesJson, a.EvidenceJson));
    }
}
