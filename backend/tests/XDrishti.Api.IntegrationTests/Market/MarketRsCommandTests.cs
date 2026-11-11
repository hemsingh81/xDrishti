using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>BK-09 criterion 8: <c>xd market rs --from D --to D2</c> reads a range and nothing else; a wrong command line is refused with a reason.</summary>
public sealed class MarketRsCommandTests
{
    [Fact]
    public void A_range_is_read()
    {
        var (from, to, errors) = MarketRsCommand.Parse(["--from", "2026-10-01", "--to", "2026-10-09"]);

        errors.ShouldBeEmpty();
        from.ShouldBe(new DateOnly(2026, 10, 1));
        to.ShouldBe(new DateOnly(2026, 10, 9));
    }

    [Theory]
    [InlineData("--from", "2026-10-01")]
    [InlineData("--from", "2026-10-09", "--to", "2026-10-01")]
    [InlineData("--from", "yesterday", "--to", "2026-10-01")]
    [InlineData("--overwrite", "--from", "2026-10-01", "--to", "2026-10-02")]
    public void A_wrong_command_line_has_a_reason(params string[] args) => MarketRsCommand.Parse(args).Errors.ShouldNotBeEmpty();
}
