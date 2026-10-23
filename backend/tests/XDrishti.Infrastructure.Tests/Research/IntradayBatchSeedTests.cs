using XDrishti.Infrastructure.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.File;

namespace XDrishti.Infrastructure.Tests.Research;

/// <summary>
/// The eight intraday strategies of P6-17a exist only as stored text (ADR 0079): each embedded seed reads, compiles on 15m with no problem, has a pinned hash and a declared parameter set.
/// A changed text is a new hash and so a new version; this test is the place that makes the change visible.
/// </summary>
public sealed class IntradayBatchSeedTests
{
    public static readonly TheoryData<string, string> Pinned = new()
    {
        { "PDH_PDL_BREAK", "a5844e0ad034b1e342a72c16dd35075e43b60a3ca87d9f3aa33c9899d64bae58" },
        { "PDH_PDL_REJECT", "d10af24055e897cfefaec76c1093862e14f1e719e30548044608f79c83377466" },
        { "VWAP_RECLAIM", "bb5d6811ea0813df6f0af3ed00c06f17ab0b96b1e437268e8f9b922929b85c49" },
        { "EMA_PULLBACK", "19d83b33a51effeeb79ecb906917188c396f437e6a75973e7510142f90e608da" },
        { "GAP_GO", "941586d44748bdbd6d61714155a62e0543f571371a752b13388316806edd3bcb" },
        { "GAP_FILL", "b73192c68e8ee5cc09df27e8eba0da0256953b9b776dd10498531e56aad725b3" },
        { "INSIDE_BAR_BO", "197d2028259feff0986282e08b248f77a208ba1ebb5cb2b41d839446ee50757d" },
        { "MEAN_REV_BANDS", "cd8e7b657792a9109c6f3cf52d283bb3cc8d19cc2a2d636983ba80ce0115d0d7" },
    };

    private static readonly SimulationEngineAdapter Engine = new();

    private static string Seed(string id) => StrategySeeder.EmbeddedTexts().Single(t => t.Name == id + ".yaml").Text;

    [Fact]
    public void The_eleven_built_ins_are_embedded_as_text()
    {
        StrategySeeder.EmbeddedTexts().Select(t => t.Name).ShouldBe(
        [
            "BASE_BREAKOUT.yaml", "EMA_PULLBACK.yaml", "GAP_FILL.yaml", "GAP_GO.yaml", "INSIDE_BAR_BO.yaml", "MEAN_REV_BANDS.yaml",
            "ORB.yaml", "PDH_PDL_BREAK.yaml", "PDH_PDL_REJECT.yaml", "VWAP_PULLBACK.yaml", "VWAP_RECLAIM.yaml",
        ]);
    }

    [Theory]
    [MemberData(nameof(Pinned))]
    public void A_seed_reads_compiles_on_15m_and_has_its_pinned_hash(string id, string hash)
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
    public void A_seed_is_intraday_15m_both_sides_with_one_fixed_rr_exit_and_a_square_off(string id, string hash)
    {
        _ = hash;
        var definition = StrategyFile.Read(Seed(id)).Definition.ShouldNotBeNull();

        definition.Id.ShouldBe(id);
        definition.Timeframes.Count.ShouldBe(1);
        definition.Sides.Count.ShouldBe(2);
        definition.Buy.ShouldNotBeNull().Exits.ShouldNotBeNull().Count.ShouldBe(1);
        definition.Sell.ShouldNotBeNull().Exits.ShouldNotBeNull().Count.ShouldBe(1);
        definition.TimeRules.ShouldNotBeNull().SquareOff.ShouldNotBeNull();
        definition.Params.Count.ShouldBeLessThanOrEqualTo(4);
    }
}
