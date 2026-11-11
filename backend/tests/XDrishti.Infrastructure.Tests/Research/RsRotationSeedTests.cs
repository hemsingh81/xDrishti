using XDrishti.Infrastructure.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;

namespace XDrishti.Infrastructure.Tests.Research;

/// <summary>BK-09 criterion 10: <c>RS_ROTATION</c> exists as stored text only, passes the compiler on 1D, is BUY only with one FIXED_RR exit and a hold of five sessions, and carries the fixed thresholds 0.67 and 3 (changing either is a new variant).</summary>
public sealed class RsRotationSeedTests
{
    private const string PinnedHash = "74880f038afa4233115932d99219b7d6a09b8994cbc5803f8a88f3c43696f020";

    private static readonly SimulationEngineAdapter Engine = new();

    private static string Seed => StrategySeeder.EmbeddedTexts().Single(t => t.Name == "RS_ROTATION.yaml").Text;

    [Fact]
    public void The_seed_reads_compiles_on_1d_and_has_its_pinned_hash()
    {
        var read = StrategyFile.Read(Seed);
        var described = Engine.Describe(Seed);

        read.HasErrors.ShouldBeFalse(string.Join("; ", read.Problems.Select(p => p.Code + " " + p.Message)));
        described.IsSuccess.ShouldBeTrue(described.IsFailure ? string.Join("; ", described.Error.Fields!.Select(f => f.Field + " " + f.Code + " " + f.Message)) : string.Empty);
        StrategyHash.Of(read.Definition!).ShouldBe(PinnedHash);
        described.Value.Hash.ShouldBe(PinnedHash);
    }

    [Fact]
    public void The_strategy_is_swing_1d_buy_only_with_one_fixed_rr_exit_a_hold_of_five_sessions_and_the_fixed_thresholds()
    {
        var definition = StrategyFile.Read(Seed).Definition.ShouldNotBeNull();

        definition.Timeframes.Count.ShouldBe(1);
        definition.Sides.Count.ShouldBe(1);
        definition.Sell.ShouldBeNull();
        definition.Buy.ShouldNotBeNull().Exits.ShouldNotBeNull().Count.ShouldBe(1);
        definition.TimeRules.ShouldNotBeNull().MaxHoldSessions.ShouldBe(5);
        Seed.ShouldContain("mom_rank(\\\"Nifty 100\\\", 126, 21) >= 0.67");
        Seed.ShouldContain("sector_rs(63) <= 3");
    }
}
