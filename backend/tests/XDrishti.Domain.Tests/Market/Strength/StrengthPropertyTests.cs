using System.Text.Json;
using XDrishti.Domain.Market.Strength;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Market.Strength;

/// <summary>BK-09 criterion 6 and the permutation property: no look-ahead, and the order of members changes nothing.</summary>
public sealed class StrengthPropertyTests
{
    private static readonly DateOnly Day0 = new(2025, 1, 1);

    private static DateOnly D(int i) => Day0.AddDays(i);

    private static MemberSeries Walk(Random rng, string symbol, int days, double? poisonAfter = null, int after = int.MaxValue)
    {
        var closes = new List<DailyClose>();
        var c = 50 + (rng.NextDouble() * 100);
        for (var i = 0; i < days; i++)
        {
            c *= 1 + ((rng.NextDouble() - 0.5) / 10);
            closes.Add(new DailyClose(D(i), i > after && poisonAfter is { } p ? p : c));
        }

        return new MemberSeries(symbol, closes, [], false, false);
    }

    private static string Everything(IReadOnlyList<MemberSeries> members, IReadOnlyList<DailyClose> index, int k)
    {
        var options = new StrengthOptions();
        var inputs = new List<MomentumInput>();
        foreach (var m in members)
        {
            var (v, _) = RelativeStrengthRank.Momentum(m, D(k), 60, 10);
            inputs.Add(v!);
        }

        var rank = RelativeStrengthRank.Rank(inputs, options);
        var vsIndex = members.Select(m => IndexRelative.Compute(m.Closes, index, D(k), 20)).ToList();
        var sectors = SectorStrength.Compute(new Dictionary<string, IReadOnlyList<MemberSeries>> { ["X"] = [.. members.Take(6)], ["Y"] = [.. members.Skip(6)] }, D(k), [21, 63], options);
        return JsonSerializer.Serialize(new { rank, vsIndex, sectors });
    }

    [Fact]
    public void Cutting_or_replacing_every_bar_after_day_k_changes_no_value_or_rank_up_to_day_k()
    {
        foreach (var seed in Enumerable.Range(1, 20))
        {
            var rng = new Random(seed);
            const int days = 200;
            const int k = 120;
            var members = Enumerable.Range(0, 14).Select(i => Walk(rng, "S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), days)).ToList();
            var index = Walk(rng, "N", days).Closes;
            var full = Everything(members, index, k);

            var cut = Everything([.. members.Select(m => m with { Closes = [.. m.Closes.Where(c => c.Date <= D(k))] })], [.. index.Where(c => c.Date <= D(k))], k);
            var poisoned = Everything([.. members.Select(m => m with { Closes = [.. m.Closes.Select(c => c.Date > D(k) ? c with { Close = 1e9 } : c)] })], [.. index.Select(c => c.Date > D(k) ? c with { Close = 1e9 } : c)], k);

            cut.ShouldBe(full);
            poisoned.ShouldBe(full);
        }
    }

    [Fact]
    public void Permuting_the_members_leaves_every_score_and_rank_unchanged()
    {
        var rng = new Random(7);
        var inputs = Enumerable.Range(0, 15).Select(i => new MomentumInput("S" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture), rng.NextDouble() - 0.4, 0.01 + (rng.NextDouble() / 20))).ToList();

        var a = RelativeStrengthRank.Rank(inputs, new StrengthOptions());
        var b = RelativeStrengthRank.Rank([.. inputs.OrderBy(_ => rng.Next())], new StrengthOptions());

        JsonSerializer.Serialize(b).ShouldBe(JsonSerializer.Serialize(a));
    }
}
