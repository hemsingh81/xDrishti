using XDrishti.Infrastructure.Research;
using XDrishti.Strategies.File;
using XDrishti.Strategies.Mirror;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Infrastructure.Tests.Research;

/// <summary>Spec P3-02e criterion 1: the mirror of the BUY side of each stored intraday strategy equals its hand-written SELL block, section by section, as trees.</summary>
public sealed class MirrorGoldenTests
{
    public static readonly TheoryData<string> Ids =
    [
        "ORB", "VWAP_PULLBACK", "GAP_FILL", "GAP_GO", "PDH_PDL_BREAK", "PDH_PDL_REJECT", "MEAN_REV_BANDS", "EMA_PULLBACK", "VWAP_RECLAIM", "INSIDE_BAR_BO",
    ];

    private static StrategyDefinition Seed(string id) =>
        StrategyFile.Read(StrategySeeder.EmbeddedTexts().Single(t => t.Name == id + ".yaml").Text).Definition.ShouldNotBeNull();

    private static List<Expression?> Trees(ConditionList? list) => [.. (list?.Items ?? []).Select(i => ConditionParser.Parse(i).Tree)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void The_mirror_of_the_buy_side_equals_the_hand_written_sell_side(string id)
    {
        var definition = Seed(id);
        var names = definition.Params.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var (mirrored, problems) = SideMirror.Mirror(definition.Buy.ShouldNotBeNull(), names);

        problems.ShouldBeEmpty();
        var hand = definition.Sell.ShouldNotBeNull();
        var rules = mirrored.ShouldNotBeNull();
        (rules.Setup?.Mode).ShouldBe(hand.Setup?.Mode);
        Trees(rules.Setup).ShouldBe(Trees(hand.Setup));
        rules.Trigger!.Mode.ShouldBe(hand.Trigger!.Mode);
        Trees(rules.Trigger).ShouldBe(Trees(hand.Trigger));
        Trees(rules.Invalidate).ShouldBe(Trees(hand.Invalidate));
        rules.Entry.ShouldBe(hand.Entry);
        rules.Stop.ShouldBe(hand.Stop);
        rules.Exits.ShouldBe(hand.Exits);
        rules.Overlays.ShouldBe(hand.Overlays);
    }
}
