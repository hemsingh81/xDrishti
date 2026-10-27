using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>Spec P9-07b: the command line of <c>xd assistant draft</c> takes one quoted idea and --show; there is no confirm option.</summary>
public sealed class AssistantDraftOptionsTests
{
    [Fact]
    public void One_idea_and_the_show_flag_are_read()
    {
        var (options, errors) = AssistantDraftOptions.Parse(["buy above VWAP", "--show"]);

        errors.ShouldBeEmpty();
        options.ShouldBe(new AssistantDraftOptions("buy above VWAP", true));
        AssistantDraftOptions.Parse(["an idea"]).Options!.Show.ShouldBeFalse();
    }

    [Theory]
    [InlineData]
    [InlineData("--show")]
    [InlineData("one", "two")]
    [InlineData("an idea", "--confirm")]
    [InlineData("an idea", "--yes")]
    public void A_missing_idea_a_second_idea_or_any_other_option_is_a_usage_error(params string[] args)
    {
        var (options, errors) = AssistantDraftOptions.Parse(args);

        options.ShouldBeNull();
        errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void The_usage_says_there_is_no_confirm_verb() => AssistantDraftOptions.Usage.ShouldContain("no confirm verb");
}
