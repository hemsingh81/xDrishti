using System.Collections;
using System.Diagnostics;
using System.Text.Json;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>BK-06 properties: no look-ahead, honest states, determinism and speed (acceptance criteria 12 to 14).</summary>
public sealed class BasketAnalyticsPropertyTests
{
    private static readonly BasketProfileOptions Options = new();

    private static DateOnly D(int i) => BasketAnalyticsTests.D(i);

    private static MemberSeries Walk(Random rng, string symbol, int days, int start = 0, double? poison = null)
    {
        var closes = new List<DailyClose>(days);
        var c = 50 + (rng.NextDouble() * 200);
        for (var i = start; i < days; i++)
        {
            c *= 1 + ((rng.NextDouble() - 0.5) / 20);
            closes.Add(new DailyClose(D(i), poison is { } p && i > 40 ? p : c));
        }

        var volumes = closes.Select(x => new DailyVolume(x.Date, 1000 + rng.Next(5000))).ToList();
        return new MemberSeries(symbol, closes, volumes, rng.Next(2) == 0, rng.Next(5) == 0);
    }

    private static BasketProfile Run(IReadOnlyList<MemberSeries> members, IReadOnlyList<DailyClose> benchmark, int lastDay, Dictionary<DateOnly, int?>? regimes = null)
    {
        var set = members.Select(m => m.Symbol).ToHashSet(StringComparer.Ordinal);
        return BasketAnalytics.Profile(new BasketProfileInput(members, _ => set, [D(0)], benchmark, regimes ?? [], new ProfileWindow(D(0), D(lastDay))), Options);
    }

    private static string Digest(BasketProfile p) => JsonSerializer.Serialize(p);

    private static IReadOnlyList<DailyClose> Bench(Random rng, int days) => Walk(rng, "N", days).Closes;

    [Fact]
    public void Truncating_or_changing_values_after_day_k_changes_no_figure_up_to_day_k()
    {
        var rng = new Random(11);
        for (var round = 0; round < 8; round++)
        {
            var k = 100 + rng.Next(60);
            var seed = rng.Next();
            IReadOnlyList<MemberSeries> Members(Func<MemberSeries, MemberSeries> shape) => [.. Enumerable.Range(0, 7).Select(i => shape(Walk(new Random(seed + i), $"M{i}", 200)))];
            var bench = Bench(new Random(seed - 1), 200);
            var full = Run(Members(m => m), bench, k);
            var cut = Run(Members(m => m with { Closes = [.. m.Closes.Where(c => c.Date <= D(k))], Volumes = [.. m.Volumes.Where(v => v.Date <= D(k))] }), [.. bench.Where(c => c.Date <= D(k))], k);
            var changed = Run(Members(m => m with { Closes = [.. m.Closes.Select(c => c.Date > D(k) ? c with { Close = 1.0 } : c)] }), [.. bench.Select(c => c.Date > D(k) ? c with { Close = 7.0 } : c)], k);
            var later = Members(m => m).Append(Walk(new Random(seed + 99), "LATE", 200, start: k + 1)).ToList();

            var longer = Run(Members(m => m), bench, 199);
            longer.Index.Points.Where(p => p.Date <= D(k)).Select(p => (p.Date, p.Level, p.Return, p.Members)).ShouldBe(PointsOf(cut));
            Digest(cut).ShouldBe(Digest(full));
            Digest(changed).ShouldBe(Digest(full));
            Digest(Run(later, bench, k)).ShouldBe(Digest(full));
        }
    }

    private static List<(DateOnly, double, double, int)> PointsOf(BasketProfile p) => [.. p.Index.Points.Select(x => (x.Date, x.Level, x.Return, x.Members))];

    [Fact]
    public void Scaling_every_close_by_a_constant_leaves_the_index_unchanged()
    {
        var rng = new Random(3);
        var members = Enumerable.Range(0, 5).Select(i => Walk(rng, $"M{i}", 60)).ToArray();
        var scaled = members.Select(m => m with { Closes = [.. m.Closes.Select(c => c with { Close = c.Close * 37 })] }).ToArray();

        var a = BasketAnalyticsTests.Build(members, 59);
        var b = BasketAnalyticsTests.Build(scaled, 59);

        a.Points.Select(p => p.Level).ToArray().ShouldBe(b.Points.Select(p => p.Level).ToArray(), 1e-9);
    }

    [Fact]
    public void A_number_exists_only_in_the_value_states_and_nothing_non_finite_escapes()
    {
        var rng = new Random(5);
        var bench = Bench(rng, 120);
        var cases = new List<(IReadOnlyList<MemberSeries> Members, IReadOnlyList<DailyClose> Bench, int Last)>
        {
            ([], bench, 119),
            ([Walk(rng, "A", 1), Walk(rng, "B", 1)], bench, 0),
            ([.. Enumerable.Range(0, 6).Select(i => new MemberSeries($"E{i}", [.. Enumerable.Range(0, 100).Select(d => new DailyClose(D(d), 100))], [], false, false))], [.. Enumerable.Range(0, 100).Select(d => new DailyClose(D(d), 100))], 99),
            ([.. Enumerable.Range(0, 6).Select(i => Walk(rng, $"N{i}", 120, poison: double.NaN))], bench, 119),
            ([.. Enumerable.Range(0, 6).Select(i => Walk(rng, $"X{i}", 120, poison: -5))], bench, 119),
            ([.. Enumerable.Range(0, 6).Select(i => Walk(rng, $"I{i}", 120, poison: double.PositiveInfinity))], [new DailyClose(D(0), double.NaN)], 119),
        };
        for (var i = 0; i < 20; i++)
        {
            cases.Add(([.. Enumerable.Range(0, 1 + rng.Next(9)).Select(j => Walk(rng, $"R{j}", 20 + rng.Next(280)))], Bench(rng, 300), 19 + rng.Next(280)));
        }

        foreach (var (members, benchmark, last) in cases)
        {
            var profile = Run(members, benchmark, last);

            foreach (var m in Metrics(profile))
            {
                (m.State is MetricState.Value or MetricState.Provisional).ShouldBe(m.Number is not null);
                (m.Number is null || double.IsFinite(m.Number.Value)).ShouldBeTrue();
            }

            Should.NotThrow(() => Digest(profile));
        }
    }

    [Fact]
    public void A_bad_close_is_dropped_and_counted()
    {
        var rng = new Random(9);
        var members = Enumerable.Range(0, 4).Select(i => Walk(rng, $"M{i}", 40)).ToArray();
        var closes = members[0].Closes.ToList();
        closes[10] = closes[10] with { Close = -1 };
        closes[11] = closes[11] with { Close = double.NaN };
        members[0] = members[0] with { Closes = closes };

        BasketAnalyticsTests.Build(members, 39).DroppedCloses.ShouldBe(2);
    }

    [Fact]
    public void The_same_input_gives_the_same_result_and_the_profile_is_fast()
    {
        var rng = new Random(21);
        var fifty = Enumerable.Range(0, 50).Select(i => Walk(rng, $"M{i:D3}", 300)).ToList();
        var twoHundred = Enumerable.Range(0, 200).Select(i => Walk(rng, $"M{i:D3}", 300)).ToList();
        var bench = Bench(rng, 300);
        Run(fifty, bench, 299);
        Run(twoHundred, bench, 299);

        Digest(Run(fifty, bench, 299)).ShouldBe(Digest(Run(fifty, bench, 299)));
        var watch = Stopwatch.StartNew();
        Run(fifty, bench, 299);
        var small = watch.Elapsed;
        watch.Restart();
        Run(twoHundred, bench, 299);
        var large = watch.Elapsed;

        small.TotalMilliseconds.ShouldBeLessThan(150);
        large.TotalMilliseconds.ShouldBeLessThan(1000);
    }

    private static List<Metric> Metrics(object? root)
    {
        var found = new List<Metric>();
        Walk(root, found, 0);
        return found;
    }

    private static void Walk(object? node, List<Metric> found, int depth)
    {
        if (node is null || depth > 12 || node is string || node.GetType().IsPrimitive || node.GetType().IsEnum || node is DateOnly)
        {
            return;
        }

        if (node is Metric m)
        {
            found.Add(m);
        }
        else if (node is IDictionary dictionary)
        {
            foreach (var value in dictionary.Values)
            {
                Walk(value, found, depth + 1);
            }
        }
        else if (node is IEnumerable items)
        {
            foreach (var item in items)
            {
                Walk(item, found, depth + 1);
            }
        }
        else if (node.GetType().Namespace == "XDrishti.Domain.Reporting")
        {
            foreach (var p in node.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
            {
                Walk(p.GetValue(node), found, depth + 1);
            }
        }
    }
}
