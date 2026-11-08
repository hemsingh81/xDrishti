using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Research;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Tests.Research;

/// <summary>CF-05, ADR 0230: the threshold of min_turnover is part of the fill rules, so of the run record and the key, only when the strategy names the filter; the lookup is read only then; defaults of the tier settings leave a conservative run as it was.</summary>
public sealed class MinTurnoverRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IEntryTierSource Tiers()
    {
        var tiers = Substitute.For<IEntryTierSource>();
        tiers.TurnoverForAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new Func<DateOnly, decimal?>(_ => 1m));
        tiers.ForAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new Func<DateOnly, LiquidityTier>(_ => LiquidityTier.A));
        return tiers;
    }

    [Fact]
    public async Task A_strategy_that_does_not_name_the_filter_keeps_its_fill_rules_its_key_and_never_asks_for_the_turnover()
    {
        var fixture = new RunFixture { CostStress = new CostStressOptions() with { MinTurnoverCrore = 30m } };
        var tiers = Tiers();
        fixture.EntryTiers = tiers;
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([abb]), Ct);

        result.Value.Fills.ShouldBe(FillRules.Default.ToCanonicalJson());
        fixture.Engine.Prepared.Count.ShouldBe(1);
        await tiers.DidNotReceive().TurnoverForAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_strategy_that_names_the_filter_is_prepared_with_the_threshold_records_it_and_another_threshold_is_another_run()
    {
        var keys = new List<string>();
        foreach (var crore in new[] { 20m, 30m })
        {
            var fixture = new RunFixture { CostStress = new CostStressOptions() with { MinTurnoverCrore = crore } };
            var tiers = Tiers();
            fixture.EntryTiers = tiers;
            fixture.Engine.ReadsTurnover = true;
            var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

            var result = await fixture.RunAsync(RunFixture.List([abb]), Ct);

            result.IsSuccess.ShouldBeTrue();
            result.Value.Fills.ShouldContain($"\"min_turnover_crore\":{crore}");
            fixture.Engine.Prepared.Last().Fills!.MinTurnoverCrore.ShouldBe(crore);
            fixture.Engine.Replays.ShouldAllBe(i => i.TurnoverOn != null);
            await tiers.Received(1).TurnoverForAsync(abb, RunFixture.D1, RunFixture.D3, Arg.Any<CancellationToken>());
            keys.Add(result.Value.RunKey);
        }

        keys[0].ShouldNotBe(keys[1]);
    }

    [Fact]
    public async Task Without_a_tier_source_the_replay_gets_no_lookup_so_every_entry_is_blocked()
    {
        var fixture = new RunFixture();
        fixture.Engine.ReadsTurnover = true;
        var abb = fixture.Add("NSE:ABB", RunFixture.ThreeDays());

        var result = await fixture.RunAsync(RunFixture.List([abb]), Ct);

        result.IsSuccess.ShouldBeTrue();
        fixture.Engine.Replays.ShouldAllBe(i => i.TurnoverOn == null);
    }

    [Fact]
    public async Task The_tier_bps_settings_are_used_by_a_conservative_run_and_their_defaults_leave_its_text_as_it_was()
    {
        var plain = new RunFixture();
        var wide = new RunFixture { CostStress = new CostStressOptions() with { TierABps = 4m } };
        var fills = new FillRulesInput(0.1, 2, 0.10, 2, 0, true);
        var a = plain.Add("NSE:ABB", RunFixture.ThreeDays());
        var b = wide.Add("NSE:ABB", RunFixture.ThreeDays());

        var byDefault = await plain.RunAsync(RunFixture.List([a]) with { Fills = fills }, Ct);
        var widened = await wide.RunAsync(RunFixture.List([b]) with { Fills = fills }, Ct);

        byDefault.Value.Fills.ShouldNotContain("tier_a_bps");
        byDefault.Value.Fills.ShouldContain("\"slippage_tiers\":\"conservative\"");
        widened.Value.Fills.ShouldContain("\"tier_a_bps\":4");
    }
}
