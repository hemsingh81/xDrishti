using System.Diagnostics;
using XDrishti.Application.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>BK-07 criterion 10 on the in-memory ports: 6 baskets of 50 members with 300 bars. Wall-clock, so it runs only when asked (the gate's load would make it flaky).</summary>
public sealed class BasketProfilePerformanceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact(Explicit = true)]
    public async Task Six_baskets_of_fifty_members_and_300_bars_rank_inside_1500_ms_and_one_profile_inside_600_ms()
    {
        var world = new ProfileWorld();
        var days = Enumerable.Range(0, 450).Select(i => new DateOnly(2025, 1, 1).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Take(300).ToList();
        Guid Series(string name) => world.Instrument(name, days.Select((d, i) => (d, 100 * Math.Exp(0.0004 * i) * (1 + (0.01 * Math.Sin((i * 0.3) + name.GetHashCode(StringComparison.Ordinal) % 7))))));
        world.IndexIsBenchmark(Series("NIFTY"));
        var baskets = Enumerable.Range(0, 6).Select(b => world.Basket($"B{b}", null, [.. Enumerable.Range(0, 50).Select(m => Series($"S{b}_{m}"))])).ToList();
        var from = days[0].ToString("yyyy-MM-dd");
        var to = days[^1].ToString("yyyy-MM-dd");

        var ranking = Stopwatch.StartNew();
        var ranked = await world.Ranking().HandleAsync(new GetBasketRankingQuery(string.Join(',', baskets), from, to, null), Ct);
        ranking.Stop();
        var profile = Stopwatch.StartNew();
        var one = await world.Profile().HandleAsync(new GetBasketProfileQuery(baskets[0].ToString("D"), from, to, null, null), Ct);
        profile.Stop();

        ranked.IsSuccess.ShouldBeTrue();
        one.IsSuccess.ShouldBeTrue();
        ranking.ElapsedMilliseconds.ShouldBeLessThan(1500);
        profile.ElapsedMilliseconds.ShouldBeLessThan(600);
    }
}
