using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Tests.Pairs.Study;

/// <summary>P14-13: the pairs family has a list of its own; the P15 phase check keeps iterating the P15 families only.</summary>
public sealed class PairFamiliesTests
{
    [Fact]
    public void The_family_is_known_to_the_pairs_list_and_not_to_the_p15_list()
    {
        PairFamilies.IsKnown("pairs:intraday-1").ShouldBeTrue();
        PairFamilies.All.ShouldBe(["pairs:intraday-1"]);
        ExtensionFamilies.IsKnown("pairs:intraday-1").ShouldBeFalse();
        ExtensionFamilies.All.ShouldNotContain("pairs:intraday-1");
        (PairFamilies.Protocol, PairFamilies.VersionKey).ShouldBe(("S", "pairs-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pairs:intraday-2")]
    [InlineData("PAIRS:INTRADAY-1")]
    [InlineData("pead")]
    public void Any_other_text_is_not_a_pairs_family(string? family)
    {
        PairFamilies.IsKnown(family).ShouldBeFalse();
    }
}
