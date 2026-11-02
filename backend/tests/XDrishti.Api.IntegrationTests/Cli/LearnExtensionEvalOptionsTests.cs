using XDrishti.Api.Cli;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd learn extension-eval</c> (spec P15-10): pure parsing, no database.</summary>
public sealed class LearnExtensionEvalOptionsTests
{
    [Fact]
    public void The_family_list_of_the_verb_is_the_domain_list()
    {
        LearnExtensionEvalOptions.Families.ShouldBe(ExtensionFamilies.All);
    }

    [Theory]
    [InlineData("filter:results")]
    [InlineData("ext:ev-1")]
    [InlineData("ext:fu-1")]
    [InlineData("ext:llm-1")]
    [InlineData("pead")]
    public void Each_family_parses_and_the_hold_out_is_off_unless_named(string family)
    {
        var (options, errors) = LearnExtensionEvalOptions.Parse(["--family", family]);

        errors.ShouldBeEmpty();
        options.ShouldBe(new LearnExtensionEvalOptions(family, false));
    }

    [Fact]
    public void The_holdout_flag_is_an_explicit_act()
    {
        LearnExtensionEvalOptions.Parse(["--holdout", "--family", "pead"]).Options.ShouldBe(new LearnExtensionEvalOptions("pead", true));
    }

    [Theory]
    [InlineData]
    [InlineData("--family")]
    [InlineData("--family", "ext:md-1")]
    [InlineData("--family", "pead", "--family", "pead")]
    [InlineData("--family", "pead", "--hold-out")]
    [InlineData("pead")]
    public void A_missing_unknown_or_repeated_argument_is_refused(params string[] args)
    {
        var (options, errors) = LearnExtensionEvalOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }
}
