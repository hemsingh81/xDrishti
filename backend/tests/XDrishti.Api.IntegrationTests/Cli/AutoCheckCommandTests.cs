using XDrishti.Api.Cli;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>The command line of <c>xd auto check</c> (spec AU-19): no database.</summary>
public sealed class AutoCheckCommandTests
{
    [Fact]
    public async Task An_argument_is_a_usage_error()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await AutoCheckCommand.RunAsync(["--now"], null!, new SimulateConsole(output, error), TestContext.Current.CancellationToken);

        code.ShouldBe(XdCommand.UsageError);
        error.ToString().ShouldContain("usage:");
    }
}
