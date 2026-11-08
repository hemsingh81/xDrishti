using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>CF-05, D2 (ADR 0229): the tier bps, the canonical text that changes only with the option, and the run options text.</summary>
public sealed class TieredSlippageTests
{
    [Fact]
    public void Tier_A_is_3_bps_tier_B_8_and_tier_C_and_an_unavailable_tier_are_not_priced()
    {
        TieredSlippage.BpsOf(LiquidityTier.A).ShouldBe(3m);
        TieredSlippage.BpsOf(LiquidityTier.B).ShouldBe(8m);
        TieredSlippage.BpsOf(LiquidityTier.C).ShouldBeNull();
        TieredSlippage.BpsOf(LiquidityTier.Unavailable).ShouldBeNull();
    }

    [Fact]
    public void The_option_changes_the_canonical_text_only_when_it_is_used()
    {
        var plain = FillRules.Create(0.1, SlippageConfig.Default).Value;
        var spelledOff = FillRules.Create(0.1, SlippageConfig.Default, 0, false).Value;
        var conservative = FillRules.Create(0.1, SlippageConfig.Default, 0, true).Value;

        spelledOff.ToCanonicalJson().ShouldBe(plain.ToCanonicalJson());
        plain.ToCanonicalJson().ShouldNotContain("slippage_tiers");
        conservative.ToCanonicalJson().ShouldBe(plain.ToCanonicalJson()[..^1] + ",\"slippage_tiers\":\"conservative\"}");
        conservative.ConservativeSlippage.ShouldBeTrue();
    }

    [Fact]
    public void The_tier_bps_and_the_threshold_enter_the_canonical_text_only_when_used()
    {
        var plain = FillRules.Create(0.1, SlippageConfig.Default).Value.ToCanonicalJson();
        var defaults = FillRules.Create(0.1, SlippageConfig.Default, 0, true, 3m, 8m).Value.ToCanonicalJson();
        var wider = FillRules.Create(0.1, SlippageConfig.Default, 0, true, 4m, 8m).Value;
        var ignored = FillRules.Create(0.1, SlippageConfig.Default, 0, false, 5m, 15m).Value.ToCanonicalJson();
        var filtered20 = FillRules.Create(0.1, SlippageConfig.Default, 0, false, 3m, 8m, 20m).Value.ToCanonicalJson();
        var filtered30 = FillRules.Create(0.1, SlippageConfig.Default, 0, false, 3m, 8m, 30m).Value.ToCanonicalJson();

        defaults.ShouldBe(plain[..^1] + ",\"slippage_tiers\":\"conservative\"}", "default bps leave the text of a conservative run as it was");
        wider.ToCanonicalJson().ShouldContain("\"tier_a_bps\":4,\"tier_b_bps\":8");
        ignored.ShouldBe(plain, "bps do not matter without the option");
        filtered20.ShouldBe(plain[..^1] + ",\"min_turnover_crore\":20}");
        filtered30.ShouldNotBe(filtered20, "another threshold is another run");
        wider.BpsOf(LiquidityTier.A).ShouldBe(4m);
        FillRules.Create(0.1, SlippageConfig.Default, 0, false, 3m, 8m, 0m).IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData(20, 20, true)]
    [InlineData(20, 19.99, false)]
    [InlineData(20, 20.01, true)]
    public void Turnover_at_the_threshold_passes_and_just_under_it_does_not(double thresholdCrore, double medianCrore, bool passes) =>
        LiquidityTiers.PassesTurnover((decimal)medianCrore * LiquidityTiers.Crore, (decimal)thresholdCrore * LiquidityTiers.Crore).ShouldBe(passes);

    [Fact]
    public void An_unknown_median_never_passes_and_a_blocked_entry_is_a_no_trade_fate()
    {
        LiquidityTiers.PassesTurnover(null, 0m).ShouldBeFalse();
        (RunFates.Empty with { TurnoverBlocked = 3 }).NoTrade.ShouldBe(3);
        (RunFates.Empty with { TurnoverBlocked = 3 }).Total.ShouldBe(3);
    }

    [Fact]
    public void Run_options_write_nothing_for_the_defaults_and_read_back_what_they_wrote()
    {
        LabRunOptions.None.ToText().ShouldBeNull();
        LabRunOptions.Parse(null).ShouldBe(LabRunOptions.None);
        foreach (var options in new[] { new LabRunOptions(true, null), new LabRunOptions(false, false), new LabRunOptions(true, true) })
        {
            LabRunOptions.Parse(options.ToText()).ShouldBe(options);
        }
    }

    [Fact]
    public void Stress_defaults_to_on_for_robust_and_off_for_the_others_and_a_spelled_default_adds_nothing_to_the_key()
    {
        LabRunOptions.None.StressOn(robust: true).ShouldBeTrue();
        LabRunOptions.None.StressOn(robust: false).ShouldBeFalse();
        new LabRunOptions(false, false).StressOn(true).ShouldBeFalse();
        new LabRunOptions(false, true).StressOn(false).ShouldBeTrue();
        new LabRunOptions(false, true).KeyText(robust: true).ShouldBeNull();
        new LabRunOptions(false, false).KeyText(robust: false).ShouldBeNull();
        new LabRunOptions(false, false).KeyText(robust: true).ShouldBe("stress=off");
        new LabRunOptions(true, null).KeyText(robust: false).ShouldBe("slippage=conservative");
    }

    [Fact]
    public void Run_fates_count_a_tier_exclusion_as_a_no_trade()
    {
        (RunFates.Empty with { TierExcluded = 2 }).NoTrade.ShouldBe(2);
        (RunFates.Empty with { TierExcluded = 2 }).Total.ShouldBe(2);
    }

    [Theory]
    [InlineData("speed=fast")]
    [InlineData("slippage=aggressive")]
    [InlineData("slippage=conservative;stress=maybe")]
    [InlineData("stress=on;stress=off")]
    [InlineData("slippage=conservative;slippage=conservative")]
    public void An_unknown_or_repeated_token_is_kept_for_the_runner_to_fail_the_run(string text) =>
        LabRunOptions.Parse(text).Unknown.ShouldNotBeNull();

    [Fact]
    public void Known_tokens_leave_nothing_unknown()
    {
        LabRunOptions.Parse("slippage=conservative;stress=off").ShouldBe(new LabRunOptions(true, false));
        LabRunOptions.Parse("slippage=conservative;stress=off").Unknown.ShouldBeNull();
    }
}
