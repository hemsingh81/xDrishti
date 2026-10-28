using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.LiveTrading;

/// <summary>Spec P11-06: the arguments of <c>xd live phase-check</c>.</summary>
public sealed class LivePhaseCheckOptionsTests
{
    [Fact]
    public void Valid_arguments_parse()
    {
        var id = Guid.NewGuid();
        var (through, account, errors) = LivePhaseCheckCommand.Parse(["--through", "2026-03-06", "--account", id.ToString()]);
        errors.ShouldBeEmpty();
        through.ShouldBe(new DateOnly(2026, 3, 6));
        account.ShouldBe(id);
    }

    [Theory]
    [InlineData("--through", "06-03-2026")]
    [InlineData("--account", "x")]
    [InlineData("--bogus", "1")]
    public void Bad_arguments_are_usage_errors(string a, string b) =>
        LivePhaseCheckCommand.Parse([a, b]).Errors.ShouldNotBeEmpty();
}
