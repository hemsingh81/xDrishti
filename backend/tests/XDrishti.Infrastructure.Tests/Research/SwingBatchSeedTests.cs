using XDrishti.Infrastructure.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;

namespace XDrishti.Infrastructure.Tests.Research;

/// <summary>
/// The four swing strategies of P6-17b exist only as stored text (ADR 0081): each embedded seed reads, compiles on 1D with no problem, has a pinned hash and a declared parameter set. A changed text is a new hash and so a new version;
/// this test is the place that makes the change visible. The hashes of the eleven earlier built-ins are pinned in their own tests and do not change.
/// </summary>
public sealed class SwingBatchSeedTests
{
    public static readonly TheoryData<string, string> Pinned = new()
    {
        { "TREND_PULLBACK", "396a48cb51d9886e99e9c0c387c901de2fea70a603cf775d57ed1bfff91cb7c2" },
        { "NR7_ID_BO", "ae23a93709c78b1fca24570863a1d284f81c0460b6a6c7e5b34f11aa6f8d1380" },
        { "RSI2_REVERSION", "e36a6764df781dabc6f43d20e50a1b2633a58ca9fbd1695073ea5c07864f69d2" },
        { "HIGH_52W_MOMENTUM", "c3a11194789b1078bb268434ad1f65f636ffadc189fb55eebd255a39f7d17f20" },
    };

    private static readonly SimulationEngineAdapter Engine = new();

    private static string Seed(string id) => StrategySeeder.EmbeddedTexts().Single(t => t.Name == id + ".yaml").Text;

    [Theory]
    [MemberData(nameof(Pinned))]
    public void A_seed_reads_compiles_on_1d_and_has_its_pinned_hash(string id, string hash)
    {
        var read = StrategyFile.Read(Seed(id));
        var described = Engine.Describe(Seed(id));

        read.HasErrors.ShouldBeFalse(string.Join("; ", read.Problems.Select(p => p.Code + " " + p.Message)));
        described.IsSuccess.ShouldBeTrue(described.IsFailure ? string.Join("; ", described.Error.Fields!.Select(f => f.Field + " " + f.Code + " " + f.Message)) : string.Empty);
        described.Value.Id.ShouldBe(id);
        StrategyHash.Of(read.Definition!).ShouldBe(hash);
        described.Value.Hash.ShouldBe(hash);
    }

    [Theory]
    [MemberData(nameof(Pinned))]
    public void A_seed_is_swing_1d_buy_only_with_one_fixed_rr_exit_and_a_hold_of_five_sessions(string id, string hash)
    {
        _ = hash;
        var definition = StrategyFile.Read(Seed(id)).Definition.ShouldNotBeNull();

        definition.Id.ShouldBe(id);
        definition.Timeframes.Count.ShouldBe(1);
        definition.Sides.Count.ShouldBe(1);
        definition.Sell.ShouldBeNull();
        definition.Buy.ShouldNotBeNull().Exits.ShouldNotBeNull().Count.ShouldBe(1);
        definition.TimeRules.ShouldNotBeNull().MaxHoldSessions.ShouldBe(5);
        definition.Params.Count.ShouldBeLessThanOrEqualTo(4);
    }
}
