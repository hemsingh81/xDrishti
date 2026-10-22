using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>The flags that name a stored strategy (spec P3-02d, D8): <c>--strategy</c> with <c>--strategy-version</c>, or <c>--strategy-hash</c> instead; each refusal is a usage error that names the flag.</summary>
public sealed class SimulateStrategyOptionsTests
{
    private const string Window = "--symbols RELIANCE --from 2026-03-02 --to 2026-03-04 --gross";

    private static readonly string Hash = new('a', 64);

    private static SimulateParse Parse(string line) => SimulateOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), ["ORB"]);

    [Fact]
    public void A_bare_id_is_the_latest_stored_version()
    {
        var options = Parse("--strategy ORB " + Window).Options!;

        options.StrategyId.ShouldBe("ORB");
        options.StrategyVersion.ShouldBeNull();
        options.StrategyHash.ShouldBeNull();
    }

    [Fact]
    public void A_version_goes_with_the_id()
    {
        var options = Parse("--strategy ORB --strategy-version 2 " + Window).Options!;

        options.StrategyVersion.ShouldBe(2);
    }

    [Fact]
    public void A_hash_stands_for_the_id_and_needs_the_timeframe()
    {
        var options = Parse("--strategy-hash " + Hash + " --timeframe 15m " + Window).Options!;

        options.StrategyHash.ShouldBe(Hash);
        options.StrategyId.ShouldBeEmpty();
        Parse("--strategy-hash " + Hash + " " + Window).Errors.ShouldContain(e => e.Contains("'--strategy-hash' needs '--timeframe'", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--strategy-version 0", "'--strategy-version' must be a whole number from 1")]
    [InlineData("--strategy-version x", "'--strategy-version' must be a whole number from 1")]
    [InlineData("--strategy-version 1.5", "'--strategy-version' must be a whole number from 1")]
    public void A_version_that_is_not_a_whole_number_from_one_is_a_usage_error(string flag, string message)
    {
        Parse("--strategy ORB " + flag + " " + Window).Errors.ShouldContain(e => e.Contains(message, StringComparison.Ordinal));
    }

    [Fact]
    public void A_version_without_an_id_or_next_to_a_hash_is_a_usage_error()
    {
        Parse("--strategy-version 1 --timeframe 15m " + Window).Errors.ShouldContain(e => e.Contains("goes with '--strategy'", StringComparison.Ordinal));
        Parse("--strategy ORB --strategy-version 1 --strategy-hash " + Hash + " --timeframe 15m " + Window).Errors.ShouldContain(e => e.Contains("a hash names the version", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void A_hash_that_is_not_64_lowercase_hex_characters_is_a_usage_error(string hash)
    {
        Parse("--strategy-hash " + hash + " --timeframe 15m " + Window).Errors.ShouldContain(e => e.Contains("64 lowercase hex", StringComparison.Ordinal));
    }

    [Fact]
    public void Neither_an_id_nor_a_hash_is_a_usage_error()
    {
        Parse(Window).Errors.ShouldContain(e => e.Contains("'--strategy' (or '--strategy-hash') is required", StringComparison.Ordinal));
    }
}
