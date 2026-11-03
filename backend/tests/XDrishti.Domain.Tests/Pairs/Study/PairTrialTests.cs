using XDrishti.Domain.Lab;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Domain.Tests.Pairs.Study;

/// <summary>P14-13 decision 1: every candidate pair is a trial, re-forming it in a later quarter is not (the text names the pair and the test, not the window); the ledger run id includes the window.</summary>
public sealed class PairTrialTests
{
    private static readonly PairStudyOptions O = PairStudyOptions.Default;

    [Fact]
    public void The_same_pair_in_another_window_has_the_same_hash_and_another_run_id()
    {
        var q1 = PairTrial.Candidate("NSE:A", "NSE:B", O, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));
        var q2 = PairTrial.Candidate("NSE:A", "NSE:B", O, new DateOnly(2024, 4, 1), new DateOnly(2025, 3, 31));
        var again = PairTrial.Candidate("NSE:A", "NSE:B", O, new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31));

        q2.Hash.ShouldBe(q1.Hash);
        q2.RunId.ShouldNotBe(q1.RunId);
        again.RunId.ShouldBe(q1.RunId);
        q1.Family.ShouldBe("pairs:intraday-1");
        q1.Hash.Length.ShouldBe(64);
    }

    [Fact]
    public void The_order_of_the_symbols_and_the_test_definition_are_part_of_the_trial()
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 12, 31);

        var ab = PairTrial.Candidate("NSE:A", "NSE:B", O, from, to);
        var ba = PairTrial.Candidate("NSE:B", "NSE:A", O, from, to);
        var lags2 = PairTrial.Candidate("NSE:A", "NSE:B", O with { Core = O.Core with { Lags = 2 } }, from, to);

        ba.Hash.ShouldNotBe(ab.Hash);
        lags2.Hash.ShouldNotBe(ab.Hash);
        ab.Text.ShouldContain("y:NSE:A");
        ab.Text.ShouldContain("x:NSE:B");
        ab.Text.ShouldContain("lags 1");
        ab.Text.ShouldContain("half-life 1..20");
    }

    [Fact]
    public void The_six_variants_are_six_distinct_trials_and_distinct_from_every_candidate()
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 12, 31);

        var hashes = PairVariant.Grid.Select(v => PairTrial.Variant(v, O, from, to).Hash).ToList();

        PairVariant.Grid.Count.ShouldBe(6);
        hashes.Distinct().Count().ShouldBe(6);
        hashes.ShouldNotContain(PairTrial.Candidate("NSE:A", "NSE:B", O, from, to).Hash);
        PairTrial.Variant(PairVariant.Primary, O, DateOnly.MinValue, DateOnly.MinValue).Hash.ShouldBe(PairTrial.Variant(new PairVariant(2.0, 5), O, from, to).Hash);
    }

    [Fact]
    public void Two_thousand_candidates_and_six_variants_make_a_factor_of_374()
    {
        MultipleTesting.Factor(2006).ShouldBe(3.74, 0.005);
        MultipleTesting.Factor(22_578 + 6).ShouldBe(4.62, 0.005);
    }

    [Theory]
    [InlineData("2.0,5", "e2.0/L5")]
    [InlineData("1.5,10", "e1.5/L10")]
    [InlineData("2.5,5", "e2.5/L5")]
    public void A_variant_of_the_grid_parses_and_has_a_stable_label(string text, string label)
    {
        PairVariant.Parse(text)!.Label.ShouldBe(label);
        PairVariant.FromLabel(label).ShouldBe(PairVariant.Parse(text));
    }

    [Theory]
    [InlineData("3.0,5")]
    [InlineData("2.0,7")]
    [InlineData("2.0")]
    [InlineData("x,5")]
    [InlineData(null)]
    public void Anything_else_is_not_a_variant(string? text)
    {
        PairVariant.Parse(text).ShouldBeNull();
    }
}
