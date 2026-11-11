using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>BK-11 criteria 8 to 11: the hold-out of a tier, the registered-basket rule, the trial count by basket and the label of a basket run.</summary>
public sealed class UniverseRulesTests
{
    private static readonly DateOnly Day = new(2026, 1, 31);

    private static readonly CellStatsOptions Options = new(RecencyHalfLifeDays: 30, PriorStrength: 20, MinTrades: 30);

    private static List<CellTrade> Spread(int wins, int losses) =>
        [.. Enumerable.Range(0, wins).Select(i => new CellTrade(Day.AddDays(-(i % 10)), 1, 0, 1)), .. Enumerable.Range(0, losses).Select(i => new CellTrade(Day.AddDays(-(i % 10)), -1, 0, 1))];

    [Fact]
    public void A_tier_that_beats_the_rest_in_both_halves_is_stable_with_its_half_counts()
    {
        var verdict = UniverseHoldOuts.Evaluate(Spread(88, 32), Spread(12, 68), 0.5, Options);

        verdict.Verdict.ShouldBe(UniverseHoldOuts.Stable);
        (verdict.FirstN + verdict.SecondN).ShouldBe(120);
        verdict.FirstN.ShouldBeGreaterThanOrEqualTo(UniverseHoldOuts.MinPerHalf);
        verdict.SecondN.ShouldBeGreaterThanOrEqualTo(UniverseHoldOuts.MinPerHalf);
        verdict.IsStable.ShouldBeTrue();
    }

    [Fact]
    public void A_tier_that_does_not_beat_the_rest_in_one_half_is_unstable()
    {
        // first half (the older days) is strong, second half is as weak as the rest
        var tier = new List<CellTrade>();
        for (var i = 0; i < 120; i++)
        {
            var older = i < 60;
            tier.Add(new CellTrade(Day.AddDays(older ? -20 - (i % 5) : -(i % 5)), older || i % 4 == 0 ? 1 : -1, 0, 1));
        }

        var verdict = UniverseHoldOuts.Evaluate(tier, Spread(30, 70), 0.5, Options);

        verdict.Verdict.ShouldBe(UniverseHoldOuts.Unstable);
    }

    [Fact]
    public void A_half_with_fewer_than_thirty_trades_is_insufficient_and_no_trade_is_insufficient_too()
    {
        UniverseHoldOuts.Evaluate(Spread(22, 8), Spread(80, 80), 0.5, Options).Verdict.ShouldBe(UniverseHoldOuts.Insufficient);
        UniverseHoldOuts.Evaluate([], Spread(1, 1), 0.5, Options).Verdict.ShouldBe(UniverseHoldOuts.Insufficient);
    }

    [Fact]
    public void The_registry_ignores_case_and_the_system_prefixes_and_a_blank_is_never_registered()
    {
        var list = UniverseSettings.Defaults.RegisteredBaskets;

        UniverseSettingsRules.IsRegistered("NSE Nifty 50", list).ShouldBeTrue();
        UniverseSettingsRules.IsRegistered("derived: most liquid", list).ShouldBeTrue();
        UniverseSettingsRules.IsRegistered("List: F&O stocks", list).ShouldBeTrue();
        UniverseSettingsRules.IsRegistered("My picks", list).ShouldBeFalse();
        UniverseSettingsRules.IsRegistered(" ", list).ShouldBeFalse();
        list.Count.ShouldBe(8);
        UniverseSettingsRules.RegisteredMessage(list).ShouldContain("Nifty 100");
    }

    [Fact]
    public void A_list_is_trimmed_deduplicated_and_bounded()
    {
        UniverseSettingsRules.ParseList(" A, b ,a,, ").ShouldBe(["A", "b"]);
        UniverseSettingsRules.ListProblem([]).ShouldNotBeNull();
        UniverseSettingsRules.ListProblem([new string('x', 61)]).ShouldNotBeNull();
        UniverseSettingsRules.ListProblem(["ok"]).ShouldBeNull();
    }

    [Fact]
    public void Eight_baskets_are_eight_variants_seven_extra_and_three_doublings_and_a_repeat_counts_once()
    {
        var trials = Enumerable.Range(1, 8).Select(i => new UniverseTrial("h", new Guid(i, 0, 0, [0, 0, 0, 0, 0, 0, 0, 1]), new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))).ToList();

        var count = UniverseTrials.Count([.. trials, trials[0], trials[3]]);

        (count.Baskets, count.Tests, count.ExtraVariants).ShouldBe((8, 8, 7));
        UniverseTrials.Count([]).ExtraVariants.ShouldBe(0);
    }

    [Fact]
    public void BK_11b_a_dated_run_on_a_basket_is_one_more_variant_and_a_repeat_counts_once()
    {
        var basket = Guid.NewGuid();
        var snapshot = new UniverseTrial("h", basket, new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31));
        var dated = snapshot with { Basis = "dated" };

        var count = UniverseTrials.Count([snapshot, dated, dated, snapshot]);

        (count.Baskets, count.Tests, count.ExtraVariants).ShouldBe((1, 2, 1));
        UniverseTrials.Count([snapshot, snapshot]).ExtraVariants.ShouldBe(0);
    }

    [Fact]
    public void A_basket_run_label_says_single_snapshot_and_survivorship_and_depends_only_on_the_basket()
    {
        var id = Guid.NewGuid();

        var label = UniverseLabels.Basket(id);

        label.ShouldContain("single_snapshot");
        label.ShouldContain("survivorship applies");
        label.ShouldContain(id.ToString("D"));
        label.ShouldNotContain("exploratory");   // registration is read when a report is built; it never enters the run key
        label.ShouldBe(UniverseLabels.Basket(id));
        label.Length.ShouldBeLessThan(200);
    }
}
