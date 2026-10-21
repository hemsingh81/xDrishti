using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09 criterion 1: the strict parser of <c>verify-pack</c> (the seed is required and strict, the count, the strategies, the cost choice, <c>--check</c> alone) and the dispatch of the verb.</summary>
public sealed class VerifyPackOptionsTests
{
    private static readonly string[] Known = ["BASE_BREAKOUT", "ORB", "VWAP_PULLBACK"];

    private const string Base = "--strategies ORB --symbols RELIANCE,TCS --from 2026-03-02 --to 2026-03-04 --seed p3-check-1 --gross";

    private static VerifyPackParse Parse(string line) => VerifyPackOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries), Known);

    public static TheoryData<string> Valid => new()
    {
        Base,
        "--strategies=ORB,VWAP_PULLBACK --basket Banks --from=2026-03-02 --to=2026-03-04 --seed=A.b_c-9 --count=100 --gross",
        Base + " --count 1",
        Base + " --acknowledge-csv-adjusted --force",
        "--strategies ORB --symbols M&M --from 2026-03-02 --to 2026-03-04 --seed s --costs fixture-a --capital 500000 --profile conservative --max-cost-r 0.2",
    };

    public static TheoryData<string, string> Invalid => new()
    {
        { Base.Replace("--seed p3-check-1 ", string.Empty, StringComparison.Ordinal), "'--seed' is required" },
        { Base + " --seed again", "'--seed' is given more than once" },
        { Base.Replace("p3-check-1", "has_a_space and more", StringComparison.Ordinal), "unexpected argument 'and'" },
        { Base.Replace("p3-check-1", "a|b", StringComparison.Ordinal), "'--seed' must be" },
        { Base.Replace("p3-check-1", new string('x', 41), StringComparison.Ordinal), "'--seed' must be" },
        { Base.Replace("p3-check-1", "naïve", StringComparison.Ordinal), "'--seed' must be" },
        { Base + " --count 0", "'--count' must be a whole number from 1 to 100" },
        { Base + " --count 101", "'--count' must be a whole number from 1 to 100" },
        { Base + " --count abc", "'--count'" },
        { Base + " --count 1.5", "'--count'" },
        { Base + " --count 020", "'--count'" },
        { Base + " --count -3", "'--count'" },
        { Base.Replace("--strategies ORB", "--strategies ORB,ORB", StringComparison.Ordinal), "names 'ORB' twice" },
        { Base.Replace("--strategies ORB", "--strategies MACD", StringComparison.Ordinal), "not a built-in strategy (ids: BASE_BREAKOUT, ORB, VWAP_PULLBACK)" },
        { Base.Replace("--strategies ORB", "--strategies orb", StringComparison.Ordinal), "case-sensitive: use 'ORB', not 'orb'" },
        { Base.Replace("--strategies ORB ", string.Empty, StringComparison.Ordinal), "'--strategies' is required" },
        { Base + " --param rr=2", "unknown option '--param'" },
        { Base + " --timeframe 5m", "unknown option '--timeframe'" },
        { Base + " --fills no-slippage", "unknown option '--fills'" },
        { Base + " --store", "unknown option '--store'" },
        { Base + " --cards 3", "unknown option '--cards'" },
        { Base.Replace("--gross", string.Empty, StringComparison.Ordinal), "--gross" },
        { Base + " --costs fixture-a", "not both" },
        { Base.Replace("--gross", "--capital 5", StringComparison.Ordinal), "never costed or gross by omission" },
        { Base + " --basket Banks", "not both" },
        { Base.Replace("--symbols RELIANCE,TCS ", string.Empty, StringComparison.Ordinal), "give --symbols or --basket" },
        { Base.Replace("2026-03-02", "02/03/2026", StringComparison.Ordinal), "'--from' must be a date" },
        { Base + " --force --force", "'--force' is given more than once" },
        { "--check log.csv --seed x", "'--check' is used alone" },
        { "--check log.csv --gross", "'--check' is used alone" },
        { "--check a.csv --check b.csv", "'--check' is given more than once" },
        { "--check", "'--check' needs a value" },
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void A_valid_command_line_gives_the_options(string line)
    {
        var parsed = Parse(line);

        parsed.Errors.ShouldBeEmpty();
        parsed.Options.ShouldNotBeNull();
    }

    [Theory]
    [MemberData(nameof(Invalid))]
    public void An_invalid_command_line_is_a_usage_error_that_names_the_flag(string line, string expected)
    {
        var parsed = Parse(line);

        parsed.Options.ShouldBeNull();
        parsed.Errors.ShouldContain(e => e.Contains(expected, StringComparison.Ordinal), string.Join(" | ", parsed.Errors));
    }

    [Fact]
    public void The_options_hold_what_was_given_with_the_defaults_of_the_count_and_no_default_of_the_seed()
    {
        var options = Parse(Base).Options!;
        var several = Parse("--strategies=ORB,VWAP_PULLBACK --basket Banks --from 2026-03-02 --to 2026-03-04 --seed s1 --count 7 --gross --force").Options!;

        (options.Seed, options.Count, options.Force, options.CheckPath).ShouldBe(("p3-check-1", 20, false, null));
        options.Strategies.ShouldBe(["ORB"]);
        options.Symbols.ShouldBe(["RELIANCE", "TCS"]);
        several.Strategies.ShouldBe(["ORB", "VWAP_PULLBACK"]);
        (several.Basket, several.Count, several.Force).ShouldBe(("Banks", 7, true));
        Parse("--check some/log.csv").Options!.CheckPath.ShouldBe("some/log.csv");
    }

    [Fact]
    public void The_usage_text_says_that_the_seed_has_no_default_and_names_the_check_mode()
    {
        VerifyPackOptions.Usage.ShouldContain("--seed <text>");
        VerifyPackOptions.Usage.ShouldContain("has no default");
        VerifyPackOptions.Usage.ShouldContain("verify-pack --check <path-to-check-log.csv>");
        VerifyPackOptions.Usage.ShouldNotContain("--param");
    }

    [Theory]
    [InlineData("verify-pack", "VerifyPack")]
    [InlineData("verify-pack --strategies ORB", "VerifyPack")]
    [InlineData("verify-pack --symbols migrate", "VerifyPack")]
    [InlineData("verify-pack --healthcheck", "VerifyPack")]
    [InlineData("simulate verify-pack", "Simulate")]
    [InlineData("x verify-pack", "Serve")]
    [InlineData("Verify-Pack", "Serve")]
    [InlineData("migrate verify-pack", "Migrate")]
    public void Only_the_first_argument_selects_the_verify_pack_verb(string line, string expected)
    {
        VerbDispatch.Of(line.Split(' ')).ToString().ShouldBe(expected);
    }

    [Fact]
    public void The_gate_mode_takes_a_pack_folder_and_only_the_second_source_folder()
    {
        var plain = Parse("--gate /p/pack");
        plain.Options!.GatePath.ShouldBe("/p/pack");
        plain.Options.AssistPath.ShouldBeNull();
        Parse("--gate /p/pack --second-source-folder /s").Options!.SecondSourceFolder.ShouldBe("/s");
        Parse("--gate /p/pack --seed x").Errors.ShouldContain(e => e.Contains("'--gate' is used with", StringComparison.Ordinal));
        Parse("--gate /p/pack --assist /p/pack").Errors.ShouldNotBeEmpty();
        Parse("--assist /p/pack").Options!.GatePath.ShouldBeNull();
        VerifyPackOptions.Usage.ShouldContain("verify-pack --gate <pack-folder>");
    }
}
