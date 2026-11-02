using System.Globalization;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Learning.Extensions;

/// <summary>Synthetic rows, sessions and a recording recipe for the tests of the feature-set comparison (P13-05).</summary>
public static class ExtensionKit
{
    public const int Base = 1;

    public static readonly ExtensionGroup Group = new("md", "md-1", ["e1"]);

    public static long Epoch(DateOnly d, int minute = 0) => new DateTimeOffset(d.Year, d.Month, d.Day, 4, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() + (minute * 60L);

    public static ValidationRow Row(string key, DateOnly date, double r, int minute, params double[] features) =>
        new(key, r > 0 ? 1 : 0, r, date, Epoch(date, minute), Epoch(date, minute) + 1800, "S|15m|BUY|intraday|X|*", [.. features]);

    public static List<DateOnly> Sessions(DateOnly from, DateOnly to)
    {
        var list = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                list.Add(d);
            }
        }

        return list;
    }

    public enum Mode
    {
        Planted,
        Placebo,
        Copy,
        Constant,
        None,
    }

    /// <summary>Rows with one base feature (weakly informative) and the extension column of <paramref name="mode"/>; R is +1 for a win and -1 for a loss.</summary>
    public static List<ValidationRow> Synthetic(int seed, DateOnly from, DateOnly to, int perDay, Mode mode)
    {
        var random = new SeededRandom((ulong)seed + 17);
        var rows = new List<ValidationRow>();
        var index = 0;
        foreach (var day in Sessions(from, to))
        {
            for (var i = 0; i < perDay; i++)
            {
                var win = random.Next(1000) < 500;
                var x0 = (win ? 0.05 : 0.0) + (random.Next(1000) / 1000.0);
                double[] features = mode switch
                {
                    Mode.Planted => [x0, (win ? 1.0 : 0.0) + (random.Next(1000) / 10000.0)],
                    Mode.Placebo => [x0, random.Next(1000) / 1000.0],
                    Mode.Copy => [x0, x0],
                    Mode.Constant => [x0, 7.0],
                    _ => [x0],
                };
                rows.Add(Row("t" + index.ToString("D6", CultureInfo.InvariantCulture), day, win ? 1.0 : -1.0, i, features));
                index++;
            }
        }

        return rows;
    }

    public static ExtensionComparisonOptions Opts() => ExtensionComparisonOptions.ForDepth() with { RecipeOptionsHash = "r1", LabelRuleVersion = "tb-1", Draws = 1000 };

    public static ExtensionGroup GroupFor(Mode mode) => mode == Mode.None ? new ExtensionGroup("md", "md-1", []) : Group;

    /// <summary>The hashes of a ledger with <paramref name="trials"/> distinct trials, this one included.</summary>
    public static string[] Ledger(ExtensionGroup group, IScorerFactory recipe, IReadOnlyList<ValidationRow> rows, ExtensionComparisonOptions options, int trials)
    {
        var from = rows.Min(r => r.TradeDate);
        var to = rows.Max(r => r.TradeDate);
        return [ExtensionTrial.Of(group, recipe.Name, options, from, to).Hash, .. Enumerable.Range(1, trials - 1).Select(i => "other-trial-" + i.ToString(CultureInfo.InvariantCulture))];
    }

    public static ExtensionReport Compare(IReadOnlyList<ValidationRow> rows, ExtensionGroup group, IScorerFactory recipe, ExtensionComparisonOptions options, IReadOnlyList<DateOnly> sessions, int trials = 1, int baseWidth = Base) =>
        ExtensionComparison.Compare(rows, baseWidth, group, recipe, Ledger(group, recipe, rows, options, trials), sessions, options).Value;

    /// <summary>Everything a stored record holds, as one text: equal texts mean equal reports and ids.</summary>
    public static string Fingerprint(ExtensionReport report)
    {
        var record = report.ToRecord("training-data-id");
        return string.Join('\n', record.EvaluationId, record.Verdict, string.Join('|', record.Missing), string.Join('|', record.Limits), record.EstimatesJson, record.EvidenceJson, record.InputsJson, record.Flips, record.Rows, record.FoldsEvaluable);
    }

    /// <summary>
    /// A deterministic recipe that scores by the buckets of the columns it is told to use: the base-rate logit plus, for each column, the logit of its bucket's rate minus the base-rate logit. A column that does not exist in a row is ignored, so
    /// the base-width view of a row reads the base columns only. A constant column is one bucket at the base rate and adds nothing. It records every fit and score.
    /// </summary>
    public sealed class ProbeFactory(params int[] columns) : IScorerFactory
    {
        public List<(int Width, string[] Keys)> Fits { get; } = [];

        public List<int> ScoredWidths { get; } = [];

        public string Name => "probe";

        public IRowScorer Fit(IReadOnlyList<ValidationRow> train)
        {
            Fits.Add((train.Count == 0 ? 0 : train[0].Features.Length, [.. train.Select(r => r.Key)]));
            var width = train.Count == 0 ? 0 : train[0].Features.Length;
            var used = columns.Where(c => c < width).ToArray();
            var baseRate = (train.Count(r => r.Label == 1) + 1.0) / (train.Count + 2.0);
            var bases = Logit(baseRate);
            var tables = used.Select(c => Table(train, c, bases)).ToArray();
            return new Scorer(this, used, tables, bases);
        }

        private static (double Min, double Width, double[] Contribution) Table(IReadOnlyList<ValidationRow> train, int column, double bases)
        {
            const int Buckets = 10;
            var min = train.Min(r => r.Features[column]);
            var max = train.Max(r => r.Features[column]);
            var width = (max - min) / Buckets;
            if (width <= 0)
            {
                return (min, 0, new double[Buckets]);
            }

            var wins = new double[Buckets];
            var count = new double[Buckets];
            foreach (var r in train)
            {
                var b = Math.Min(Buckets - 1, (int)((r.Features[column] - min) / width));
                count[b]++;
                wins[b] += r.Label;
            }

            return (min, width, wins.Select((w, b) => Logit((w + 1) / (count[b] + 2)) - bases).ToArray());
        }

        private static double Logit(double p) => Math.Log(p / (1 - p));

        private sealed class Scorer(ProbeFactory owner, int[] used, (double Min, double Width, double[] Contribution)[] tables, double bases) : IRowScorer
        {
            public double Score(ValidationRow row)
            {
                owner.ScoredWidths.Add(row.Features.Length);
                var z = bases;
                for (var i = 0; i < used.Length; i++)
                {
                    var (min, width, contribution) = tables[i];
                    if (width > 0 && used[i] < row.Features.Length)
                    {
                        z += contribution[Math.Clamp((int)((row.Features[used[i]] - min) / width), 0, contribution.Length - 1)];
                    }
                }

                return 1 / (1 + Math.Exp(-z));
            }
        }
    }
}
