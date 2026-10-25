using XDrishti.Api.Cli;

namespace XDrishti.Api.IntegrationTests.Cli;

/// <summary>Spec P8-13 criterion 9: <c>--store</c> on <c>xd learn windows</c> and <c>xd learn policy</c> is a flag without a value and is off by default.</summary>
public sealed class LearnStoreFlagTests
{
    private static readonly string Run = $"{new string('a', 64)}:intraday:X";

    [Fact]
    public void Windows_store_is_off_unless_given()
    {
        LearnWindowsOptions.Parse(["--run", Run]).Options!.Store.ShouldBeFalse();
        LearnWindowsOptions.Parse(["--run", Run, "--store"]).Options!.Store.ShouldBeTrue();
        LearnWindowsOptions.Usage.ShouldContain("--store");
    }

    [Fact]
    public void Policy_store_is_off_unless_given()
    {
        LearnPolicyOptions.Parse(["--run", Run]).Options!.Store.ShouldBeFalse();
        LearnPolicyOptions.Parse(["--run", Run, "--store", "--trials", "2"]).Options!.Store.ShouldBeTrue();
        LearnPolicyOptions.Usage.ShouldContain("--store");
    }
}
