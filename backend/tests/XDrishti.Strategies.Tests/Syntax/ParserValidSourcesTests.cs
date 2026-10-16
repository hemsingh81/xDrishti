using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Tests.Syntax;

public sealed class ParserValidSourcesTests
{
    [Fact]
    public void The_table_has_more_than_sixty_cases()
    {
        ValidSources.Cases.Count.ShouldBeGreaterThanOrEqualTo(60);
    }

    [Theory]
    [MemberData(nameof(ValidSources.Sources), MemberType = typeof(ValidSources))]
    public void A_valid_source_parses_to_the_hand_written_tree(string source)
    {
        var result = ConditionParser.Parse(source);

        result.Problems.ShouldBeEmpty();
        result.IsSuccess.ShouldBeTrue();
        result.Tree.ShouldBe(ValidSources.Cases[source]);
    }
}
