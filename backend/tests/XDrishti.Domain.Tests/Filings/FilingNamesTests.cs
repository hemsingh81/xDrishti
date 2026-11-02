using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Tests.Filings;

public sealed class FilingNamesTests
{
    [Theory]
    [InlineData("a.xml", true)]
    [InlineData("RELIANCE_results_2026-06-30_ab12cd34.xbrl", true)]
    [InlineData(".hidden.xml", false)]
    [InlineData("../x.xml", false)]
    [InlineData("a b.xml", false)]
    [InlineData("a/b.xml", false)]
    [InlineData("", false)]
    public void Names_follow_the_pattern(string name, bool valid) => FilingNames.IsValidName(name).ShouldBe(valid);

    [Fact]
    public void A_name_of_121_characters_is_refused()
    {
        FilingNames.IsValidName(new string('a', 120)).ShouldBeTrue();
        FilingNames.IsValidName(new string('a', 121)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("a.XML", ".xml")]
    [InlineData("a.csv", ".csv")]
    [InlineData("a.exe", null)]
    [InlineData("a", null)]
    public void Only_the_five_extensions_are_accepted_in_any_case(string name, string? expected) => FilingNames.AcceptedExtension(name).ShouldBe(expected);

    [Fact]
    public void A_name_ending_in_meta_json_is_a_sidecar()
    {
        FilingNames.IsSidecarName("a.xml.meta.json").ShouldBeTrue();
        FilingNames.IsSidecarName("a.xml").ShouldBeFalse();
    }
}
