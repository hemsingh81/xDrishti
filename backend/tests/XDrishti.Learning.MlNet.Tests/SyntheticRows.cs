using System.Collections.Immutable;
using XDrishti.Domain.Learning;

namespace XDrishti.Learning.MlNet.Tests;

/// <summary>Synthetic labelled rows: one trade a day, three features, a fixed seed. In the learnable set the label follows feature 0 (a linear signal); in the interaction set it follows the sign of feature 0 times feature 1 (which a logistic regression and a cell rate cannot see); in the noise set it follows nothing.</summary>
internal static class SyntheticRows
{
    private static readonly DateOnly Start = new(2023, 1, 2);

    public static List<ValidationRow> Make(int count, bool learnable, int seed = 7, long holdSeconds = 3600, bool interaction = false)
    {
        var random = new Random(seed);
        var rows = new List<ValidationRow>(count);
        for (var i = 0; i < count; i++)
        {
            var x = new[] { Gauss(random), Gauss(random), Gauss(random) };
            var p = interaction ? (x[0] * x[1] > 0 ? 0.8 : 0.2) : learnable ? 1 / (1 + Math.Exp(-2.5 * x[0])) : 0.5;
            var label = random.NextDouble() < p ? 1 : 0;
            var date = Start.AddDays(i);
            var decision = new DateTimeOffset(date.Year, date.Month, date.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
            rows.Add(new ValidationRow("k" + i.ToString("D5", System.Globalization.CultureInfo.InvariantCulture), label, label == 1 ? 1 : -1, date, decision, decision + holdSeconds, i % 2 == 0 ? "A" : "B", [.. x]));
        }

        return rows;
    }

    public static ValidationRow WithLabel(ValidationRow row, int label) => row with { Label = label };

    public static ValidationRow WithFeatures(ValidationRow row, params double[] features) => row with { Features = [.. features] };

    public static ImmutableArray<double> Features(params double[] values) => [.. values];

    private static double Gauss(Random random) => Math.Sqrt(-2 * Math.Log(1 - random.NextDouble())) * Math.Cos(2 * Math.PI * random.NextDouble());
}
