using XDrishti.Domain.Lab;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Domain.Tests.Pairs.Study;

/// <summary>P14-13: the candidate rule (same sector, correlation of daily log returns at or above 0.8), the cap and its tie rule, and the choice of y by turnover.</summary>
public sealed class PairUniverseTests
{
    private static PairMember Member(string symbol, string? sector, double[] lnPrices, double turnover = 1_000_000) =>
        new(symbol, sector, [.. lnPrices.Select(Math.Exp)], turnover);

    private static double[] Walk(ulong seed) => PairsData.Walk(new SeededRandom(seed), 252);

    private static double[] Shifted(double[] walk, double add) => [.. walk.Select(v => v + add)];

    [Fact]
    public void Only_same_sector_pairs_with_a_return_correlation_of_at_least_08_are_candidates_and_the_rest_are_counted()
    {
        var w1 = Walk(11);
        var w2 = Walk(12);
        var members = new[]
        {
            Member("NSE:AAA", "Banks", Shifted(w1, 4.6)),
            Member("NSE:BBB", "Banks", Shifted(w1, 5.0)),
            Member("NSE:CCC", "Banks", Shifted(w2, 4.6)),
            Member("NSE:DDD", "Auto", Shifted(w1, 4.6)),
            Member("NSE:EEE", null, Shifted(w1, 4.6)),
        };

        var set = PairUniverse.Candidates(members, PairStudyOptions.Default);

        set.Candidates.Select(c => c.Key).ShouldBe(["NSE:AAA|NSE:BBB"]);
        set.SameSectorPairs.ShouldBe(3);
        set.BelowCorrelation.ShouldBe(2);
        set.NoSector.ShouldBe(1);
        set.CorrelationUnavailable.ShouldBe(0);
        set.Capped.ShouldBe(0);
    }

    [Fact]
    public void Y_is_the_member_with_the_larger_mean_turnover_and_a_tie_goes_to_the_symbol_that_sorts_first()
    {
        var w = Walk(21);
        var members = new[]
        {
            Member("NSE:AAA", "Banks", Shifted(w, 4.6), turnover: 10),
            Member("NSE:BBB", "Banks", Shifted(w, 5.0), turnover: 20),
            Member("NSE:CCC", "Banks", Shifted(w, 5.2), turnover: 20),
        };

        var set = PairUniverse.Candidates(members, PairStudyOptions.Default);

        set.Candidates.Select(c => c.Key).Order(StringComparer.Ordinal).ShouldBe(["NSE:BBB|NSE:AAA", "NSE:BBB|NSE:CCC", "NSE:CCC|NSE:AAA"]);
    }

    [Fact]
    public void More_candidates_than_the_cap_keeps_the_most_correlated_and_breaks_ties_by_the_symbols_ordinal_order()
    {
        var w = Walk(31);
        var noisy = PairsData.Walk(new SeededRandom(32), 252, 0.004);
        var members = new[]
        {
            Member("NSE:AAA", "Banks", Shifted(w, 4.6)),
            Member("NSE:BBB", "Banks", Shifted(w, 4.7)),
            Member("NSE:CCC", "Banks", Shifted(w, 4.8)),
            Member("NSE:DDD", "Banks", [.. w.Select((v, i) => v + 4.6 + noisy[i])]),
        };

        var all = PairUniverse.Candidates(members, PairStudyOptions.Default);
        var capped = PairUniverse.Candidates(members, PairStudyOptions.Default with { MaxCandidates = 2 });

        all.Candidates.Count.ShouldBe(6);
        all.Candidates.Take(3).Select(c => c.Correlation).ShouldAllBe(r => r > 0.999999);
        capped.Candidates.Count.ShouldBe(2);
        capped.Capped.ShouldBe(4);
        capped.Candidates.Select(c => new[] { c.Y, c.X }.Order(StringComparer.Ordinal).First() + "|" + new[] { c.Y, c.X }.Order(StringComparer.Ordinal).Last()).ShouldBe(["NSE:AAA|NSE:BBB", "NSE:AAA|NSE:CCC"]);
    }

    [Fact]
    public void A_member_with_a_bad_price_has_no_correlation_and_every_pair_with_it_is_counted_unavailable()
    {
        var w = Walk(41);
        var bad = Shifted(w, 4.6);
        var members = new[]
        {
            Member("NSE:AAA", "Banks", Shifted(w, 4.6)),
            new PairMember("NSE:BBB", "Banks", [.. bad.Select(Math.Exp).Select((v, i) => i == 5 ? -1.0 : v)], 1),
            Member("NSE:CCC", "Banks", Shifted(w, 4.9)),
        };

        var set = PairUniverse.Candidates(members, PairStudyOptions.Default);

        set.Candidates.Select(c => c.Key).ShouldBe(["NSE:AAA|NSE:CCC"]);
        set.CorrelationUnavailable.ShouldBe(2);
    }

    [Fact]
    public void The_same_members_give_the_same_candidates_in_any_input_order()
    {
        var w = Walk(51);
        var a = Member("NSE:AAA", "Banks", Shifted(w, 4.6), 5);
        var b = Member("NSE:BBB", "Banks", Shifted(w, 4.8), 7);
        var c = Member("NSE:CCC", "Banks", Shifted(w, 5.0), 3);

        var one = PairUniverse.Candidates([a, b, c], PairStudyOptions.Default).Candidates.Select(x => x.Key).ToList();
        var two = PairUniverse.Candidates([c, a, b], PairStudyOptions.Default).Candidates.Select(x => x.Key).ToList();

        two.ShouldBe(one);
    }
}
