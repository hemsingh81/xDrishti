using XDrishti.Domain.Lab;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options.Evaluation;
using XDrishti.Domain.Tests.Pairs;

namespace XDrishti.Domain.Tests.OptionEvaluation;

/// <summary>Seeded paired trades for the protocol O tests: quarters from 2024-04-01, the cash R about 0.05 with spread 0.8, the paired difference d around a chosen mean.</summary>
internal static class ExprPairs
{
    public static readonly DateOnly Start = new(2024, 4, 1);

    public static readonly DateOnly EpochFrom = new(2025, 9, 1);

    /// <summary>One quarter after another; <paramref name="counts"/> gives the pairs of each quarter. <paramref name="shift"/> adds to d by date.</summary>
    public static List<ExpressionPair> Make(ulong seed, int[] counts, double meanD, double sdD = 0.5, bool net = true, int fallback = 0, Func<DateOnly, double>? shift = null)
    {
        var rng = new SeededRandom(seed);
        var list = new List<ExpressionPair>();
        for (var q = 0; q < counts.Length; q++)
        {
            var qs = Start.AddMonths(3 * q);
            for (var i = 0; i < counts[q]; i++)
            {
                var date = qs.AddDays(i % 80);
                var cash = 0.05 + (0.8 * PairsData.Normal(rng));
                var d = meanD + (shift?.Invoke(date) ?? 0) + (sdD * PairsData.Normal(rng));
                var entry = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), IndiaTime.Offset).AddSeconds(i);
                list.Add(new ExpressionPair(date, entry.ToUnixTimeSeconds(), entry.AddMinutes(40).ToUnixTimeSeconds(), "T" + q + "-" + i, cash + d, cash, net, list.Count < fallback, false, false, i % 2 == 0));
            }
        }

        return list;
    }

    public static int[] Even(int quarters, int each) => [.. Enumerable.Repeat(each, quarters)];

    public static ProtocolOInput Input(IReadOnlyList<ExpressionPair> pairs, int trials = 16, ExpressionStudyOptions? options = null, IReadOnlyDictionary<string, int>? unpaired = null, DateOnly? holdOutStart = null, DateOnly? epochFrom = null) =>
        new(pairs, unpaired ?? new Dictionary<string, int>(), trials, epochFrom ?? EpochFrom, holdOutStart, options ?? ExpressionStudyOptions.Default);

    public static ProtocolOResult Run(IReadOnlyList<ExpressionPair> pairs, int trials = 16, ExpressionStudyOptions? options = null, IReadOnlyDictionary<string, int>? unpaired = null, DateOnly? epochFrom = null) =>
        ProtocolO.Evaluate(Input(pairs, trials, options, unpaired, null, epochFrom)).Value;
}
