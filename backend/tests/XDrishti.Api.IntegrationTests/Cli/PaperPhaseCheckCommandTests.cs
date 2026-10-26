using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd paper phase-check</c> (spec P10-06): pure parsing, no database.</summary>
public sealed class PaperPhaseCheckCommandTests
{
    [Fact]
    public void No_options_means_the_last_closed_session_and_the_newest_epoch()
    {
        PaperPhaseCheckCommand.TryParse([], out var through, out var epoch).ShouldBeTrue();

        through.ShouldBeNull();
        epoch.ShouldBeNull();
    }

    [Fact]
    public void Through_and_epoch_are_read()
    {
        PaperPhaseCheckCommand.TryParse(["--through", "2027-01-25", "--epoch", "2"], out var through, out var epoch).ShouldBeTrue();

        through.ShouldBe(new DateOnly(2027, 1, 25));
        epoch.ShouldBe(2);
    }

    [Theory]
    [InlineData("--through", "25-01-2027")]
    [InlineData("--through", "2027-02-30")]
    [InlineData("--epoch", "-1")]
    [InlineData("--epoch", "x")]
    [InlineData("--draws", "5")]
    public void A_bad_line_is_a_usage_error(string flag, string value) =>
        PaperPhaseCheckCommand.TryParse([flag, value], out _, out _).ShouldBeFalse();

    [Fact]
    public void A_missing_value_or_a_repeated_flag_is_a_usage_error()
    {
        PaperPhaseCheckCommand.TryParse(["--through"], out _, out _).ShouldBeFalse();
        PaperPhaseCheckCommand.TryParse(["--epoch", "1", "--epoch", "2"], out _, out _).ShouldBeFalse();
    }

    [Fact]
    public void The_verb_is_found_by_its_name()
    {
        PaperPhaseCheckCommand.Handles(["phase-check"]).ShouldBeTrue();
        PaperPhaseCheckCommand.Handles(["compare"]).ShouldBeFalse();
    }
}
