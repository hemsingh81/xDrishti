using System.Diagnostics;
using XDrishti.Domain.Baskets.Derived;
using static XDrishti.Domain.Tests.Baskets.Derived.DerivedFixtures;

namespace XDrishti.Domain.Tests.Baskets.Derived;

/// <summary>Properties over all 14 baskets on seeded universes (BK-02 acceptance criteria 16, 17, 18) plus the golden ranks and the speed budget.</summary>
public sealed class DerivedPropertyTests
{
    private const int Bars = 300;

    public static TheoryData<int, int> Cases()
    {
        var data = new TheoryData<int, int>();
        foreach (var seed in new[] { 1, 2, 3 })
        {
            foreach (var id in Enumerable.Range(1, 14))
            {
                data.Add(seed, id);
            }
        }

        return data;
    }

    private static DateTimeOffset? Formation(int id) => id switch
    {
        4 => At(Bars, 9, 20),
        12 => At(Bars, 9, 30),
        _ => null,
    };

    /// <summary>Today's bars before the formation time (all that is knowable) added to every instrument when the basket forms intraday.</summary>
    private static List<InstrumentBars> WithToday(List<InstrumentBars> universe, int id) =>
        [.. universe.Select(i => Formation(id) is { } f ? i with { Intraday = [.. i.Intraday!, .. Session(Bars, 20, i.Daily[^1].Close, i.Daily[^1].Close * 1.003, i.Daily[^1].Close * 0.997, 120).Where(b => b.Time < f)] } : i)];

    /// <summary>The same universe plus everything the engine must not read: daily bars after as_of and intraday bars at or after the formation time.</summary>
    private static List<InstrumentBars> WithFuture(List<InstrumentBars> universe, int id) =>
        [.. universe.Select(i =>
        {
            Bar[] future = [new(Day(Bars), 1, 1e6, 0.001, 1e5, 1e9), new(Day(Bars + 1), 7, 7e6, 0.002, 1e6, 1e9)];
            var cutoff = Formation(id) ?? At(Bars, 0, 0);
            var later = Session(Bars, 20, 5000, 9000, 1, 9_999_999).Where(b => b.Time >= cutoff).ToArray();
            var benchmark = i.Benchmark!.Concat(future).ToArray();
            return i with { Daily = [.. i.Daily, .. future], Intraday = [.. i.Intraday!, .. later], Benchmark = benchmark };
        })];

    [Theory]
    [MemberData(nameof(Cases))]
    public void Appending_a_later_bar_never_changes_the_result_for_an_earlier_as_of(int seed, int id)
    {
        var (universe, asOf) = Synthetic(40, Bars, seed);
        var known = WithToday(universe, id);

        var before = Eval(id, known, asOf, null, null, Formation(id));
        var after = Eval(id, WithFuture(known, id), asOf, null, null, Formation(id));

        Text(after).ShouldBe(Text(before));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Input_order_does_not_matter_scores_are_finite_and_every_instrument_is_counted_once(int seed, int id)
    {
        var (universe, asOf) = Synthetic(40, Bars, seed);
        var known = WithToday(universe, id);
        var p = DerivedParams.Default();

        var r = Eval(id, known, asOf, p, null, Formation(id));
        var reversed = Eval(id, [.. known.AsEnumerable().Reverse()], asOf, p, null, Formation(id));

        Text(reversed).ShouldBe(Text(r));
        r.Scores.ShouldAllBe(s => double.IsFinite(s.Score));
        r.ParamsHash.ShouldBe(p.Hash);
        var all = r.Members.Select(m => m.Symbol).Concat(r.BelowThreshold).Concat(r.Excluded.Select(e => e.Symbol)).ToList();
        all.Count.ShouldBe(known.Count);
        all.Distinct().Count().ShouldBe(known.Count);
        var k = id is 14 ? known.Count : ((IReadOnlyDictionary<string, double>)p.Values).Where(kv => kv.Key == $"Basket{id}.TopK" || kv.Key == $"Basket{id}.Cap").Select(kv => (int)kv.Value).DefaultIfEmpty(known.Count).First();
        r.Members.Count.ShouldBeLessThanOrEqualTo(k);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_same_input_gives_byte_identical_output(int seed, int id)
    {
        var (universe, asOf) = Synthetic(40, Bars, seed);
        var known = WithToday(universe, id);

        var a = Eval(id, known, asOf, null, null, Formation(id));
        var b = Eval(id, known, asOf, null, null, Formation(id));

        Text(a).ShouldBe(Text(b));
    }

    [Fact]
    public void Basket_11_intraday_variant_compares_the_cumulative_volume_to_the_same_slot_of_prior_sessions()
    {
        var daily = Flat(45, 100);
        List<IntradayBar> bars = [];
        foreach (var d in new[] { 42, 43, 44 })
        {
            bars.AddRange(Session(d, 20, volume: 100));
        }

        bars.AddRange(Session(45, 10, volume: 250));
        bars.AddRange(Session(45, 20, volume: 99_999).Skip(10));

        var r = Eval(11, [Inst("V", daily, intraday: [.. bars])], Day(44), null, null, At(45, 9, 25));

        Score(r, "V", s => s.Score).ShouldBe(2.5, 1e-12);
        r.Members.Select(m => m.Symbol).ShouldBe(["V"]);
    }

    [Fact]
    public void Golden_ranks_of_baskets_1_3_and_6_match_a_table_computed_outside_the_code()
    {
        // Instrument k (0..29): ATR% = (k + 1) %, so rank = 30 - k. ADTV = ((k x 7) mod 30 + 1) x Rs 1 crore. Momentum = ((k x 11) mod 30 + 1) %.
        var flat = Enumerable.Range(0, 30).Select(k => Inst($"K{k:D2}", FromCloses(Enumerable.Repeat(100.0, 260).ToList(), (k + 1) / 2.0, (((k * 7) % 30) + 1) * 100_000.0))).ToList();
        var moving = Enumerable.Range(0, 30).Select(k =>
        {
            var closes = Enumerable.Repeat(100.0, 260).ToArray();
            closes[260 - 21] = 100 + (((k * 11) % 30) + 1);
            return Inst($"K{k:D2}", FromCloses(closes));
        }).ToList();
        var asOf = Day(259);

        var atr = Eval(1, flat, asOf);
        var adtv = Eval(3, flat, asOf);
        var momentum = Eval(6, moving, asOf);

        for (var k = 0; k < 30; k++)
        {
            Score(atr, $"K{k:D2}", s => s.Rank).ShouldBe(30 - k);
            Score(adtv, $"K{k:D2}", s => s.Rank).ShouldBe(30 - ((k * 7) % 30));
            Score(momentum, $"K{k:D2}", s => s.Rank).ShouldBe(30 - ((k * 11) % 30));
        }
    }

    [Fact]
    public void Clustering_500_instruments_over_120_returns_stays_inside_the_ci_bound_of_3_seconds()
    {
        var (universe, asOf) = Synthetic(500, 130, 5, withIntraday: false);
        var watch = Stopwatch.StartNew();

        var r = Eval(14, universe, asOf);

        watch.Stop();
        watch.Elapsed.TotalSeconds.ShouldBeLessThan(3, $"took {watch.Elapsed.TotalSeconds:F2} s");
        (r.Members.Count + r.Excluded.Count).ShouldBe(500);
    }

    [Fact]
    public void The_daily_baskets_over_500_instruments_and_260_bars_stay_inside_a_ci_bound()
    {
        var (universe, asOf) = Synthetic(500, 260, 6, withIntraday: false);
        var watch = Stopwatch.StartNew();

        foreach (var id in new[] { 1, 2, 3, 5, 6, 7, 8, 9, 10, 11 })
        {
            Eval(id, universe, asOf);
        }

        watch.Stop();
        watch.Elapsed.TotalSeconds.ShouldBeLessThan(10, $"took {watch.Elapsed.TotalSeconds:F2} s");
    }
}
