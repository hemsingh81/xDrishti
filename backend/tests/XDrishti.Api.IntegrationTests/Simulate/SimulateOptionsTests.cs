using System.Globalization;
using XDrishti.Api.Simulate;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-04b2 criterion 3: the strict parser. A table of command lines; the valid ones give the options, the invalid ones a usage error that names the flag; the result does not depend on the machine's language.</summary>
public sealed class SimulateOptionsTests
{
    private static readonly string[] Known = ["ORB"];

    private const string Window = "--from 2026-03-02 --to 2026-03-04 --gross";

    private static string[] Line(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static SimulateParse Parse(string line) => SimulateOptions.Parse(Line(line), Known);

    public static TheoryData<string> Valid => new()
    {
        "--strategy ORB --symbols RELIANCE,TCS " + Window,
        "--strategy=ORB --symbols=RELIANCE,TCS --from=2026-03-02 --to=2026-03-04 --gross",
        "--strategy ORB --basket Banks " + Window,
        "--strategy ORB --basket 3f2504e0-4f89-41d3-9a0c-0305e82c3301 " + Window,
        "--strategy ORB --symbols M&M,BAJAJ-AUTO,NSE:TCS,INE002A01018 " + Window,
        "--strategy ORB --symbols RELIANCE " + Window + " --param or_min=30 --param rr=2.5",
        "--strategy ORB --symbols RELIANCE " + Window + " --param=rr=3",
        "--strategy ORB --symbols RELIANCE " + Window + " --timeframe 5m",
        "--strategy ORB --symbols RELIANCE " + Window + " --fills no-slippage",
        "--strategy ORB --symbols RELIANCE " + Window + " --fills=modelled",
        "--strategy ORB --symbols RELIANCE " + Window + " --acknowledge-csv-adjusted --force",
        "--strategy ORB --symbols RELIANCE " + Window + " --param rr=1e1",
        "--strategy ORB --symbols RELIANCE " + Window + " --param rr=-1.5",
        "--list-strategies",
    };

    public static TheoryData<string, string> Invalid => new()
    {
        { "--strategy ORB --symbols RELIANCE " + Window + " --cards", "--cards" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --symbols-file x.txt", "--symbols-file" },
        { "--strategy ORB --strategy ORB --symbols RELIANCE " + Window, "--strategy" },
        { "--strategy ORB --symbols RELIANCE --symbols TCS " + Window, "--symbols" },
        { "--strategy ORB --symbols RELIANCE --from 2026-03-02 --from 2026-03-03 --to 2026-03-04", "--from" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --force --force", "--force" },
        { "--strategy ORB --symbols RELIANCE --from 2026-03-02 --to", "--to" },
        { "--strategy --symbols RELIANCE " + Window, "--strategy" },
        { "--strategy ORB --symbols --from 2026-03-02 --to 2026-03-04", "--symbols" },
        { "--strategy ORB --symbols RELIANCE --basket Banks " + Window, "--basket" },
        { "--strategy ORB " + Window, "--symbols" },
        { "--symbols RELIANCE " + Window, "--strategy" },
        { "--strategy ORB --symbols RELIANCE --to 2026-03-04", "--from" },
        { "--strategy ORB --symbols RELIANCE --from 2026-03-02", "--to" },
        { "--strategy ORB --symbols RELIANCE --from 2026-13-01 --to 2026-03-04", "--from" },
        { "--strategy ORB --symbols RELIANCE --from 2026-1-1 --to 2026-03-04", "--from" },
        { "--strategy ORB --symbols RELIANCE --from 2026-03-02 --to 03/04/2026", "--to" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --param rr=NaN", "rr" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --param rr=Infinity", "rr" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --param rr=1e999", "rr" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --param rr=0x10", "rr" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --param rr=1,5", "rr" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --param Rr=2", "--param" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --param rr", "--param" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --param rr=2 --param rr=3", "rr" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --fills fast", "--fills" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --fills exact", "--fills" },
        { "--strategy orb --symbols RELIANCE " + Window, "--strategy" },
        { "--strategy ORB --symbols $(x) " + Window, "--symbols" },
        { "--strategy ORB --symbols RELIANCE;ls " + Window, "--symbols" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --timeframe 1D", "--timeframe" },
        { "--strategy ORB --symbols RELIANCE " + Window + " --force=yes", "--force" },
        { "--strategy ORB --symbols RELIANCE " + Window + " stray", "stray" },
        { "--list-strategies --force", "--list-strategies" },
        { "--healthcheck", "--healthcheck" },
        { string.Empty, "--strategy" },
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
    public void An_invalid_command_line_is_a_usage_error_that_names_the_flag(string line, string flag)
    {
        var parsed = Parse(line);

        parsed.Options.ShouldBeNull();
        parsed.Errors.ShouldNotBeEmpty();
        parsed.Errors.ShouldContain(e => e.Contains(flag, StringComparison.Ordinal), string.Join(" | ", parsed.Errors));
    }

    [Fact]
    public void The_values_are_read_into_the_options_with_the_defaults_of_the_spec()
    {
        var options = Parse("--strategy ORB --symbols RELIANCE,nse:tcs,M&M --from 2026-03-02 --to=2026-03-04 --gross --param or_min=30 --param rr=2.5").Options.ShouldNotBeNull();

        options.StrategyId.ShouldBe("ORB");
        options.Symbols.ShouldBe(["RELIANCE", "nse:tcs", "M&M"]);
        options.Basket.ShouldBeNull();
        (options.From, options.To).ShouldBe((new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4)));
        options.Timeframe.ShouldBeNull("the registry's default for the strategy is used when none is given");
        options.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (p.Key, p.Value)).ShouldBe([("or_min", 30.0), ("rr", 2.5)]);
        options.Fills.ShouldBe(FillsMode.Modelled);
        (options.AcknowledgeCsvAdjusted, options.Force, options.ListStrategies).ShouldBe((false, false, false));
    }

    [Fact]
    public void A_symbol_list_may_be_separated_by_spaces_inside_one_quoted_argument()
    {
        var options = SimulateOptions.Parse(["--strategy", "ORB", "--symbols", "RELIANCE TCS,SBIN", "--from", "2026-03-02", "--to", "2026-03-04", "--gross"], Known).Options.ShouldNotBeNull();

        options.Symbols.ShouldBe(["RELIANCE", "TCS", "SBIN"]);
    }

    [Fact]
    public void The_two_fill_modes_and_the_switches_are_read()
    {
        var options = Parse("--strategy ORB --basket Banks " + Window + " --fills no-slippage --acknowledge-csv-adjusted --force --timeframe 5m").Options.ShouldNotBeNull();

        (options.Fills, options.AcknowledgeCsvAdjusted, options.Force, options.Timeframe, options.Basket).ShouldBe((FillsMode.NoSlippage, true, true, "5m", "Banks"));
    }

    [Fact]
    public void An_unknown_strategy_id_is_not_a_usage_error_it_is_left_to_the_engine_but_a_different_case_is()
    {
        Parse("--strategy MACD --symbols RELIANCE " + Window).Options.ShouldNotBeNull();
        Parse("--strategy orb --symbols RELIANCE " + Window).Errors.ShouldContain(e => e.Contains("case-sensitive", StringComparison.Ordinal) && e.Contains("ORB", StringComparison.Ordinal));
    }

    [Fact]
    public void An_echoed_argument_has_its_control_characters_replaced_and_is_cut()
    {
        var parsed = SimulateOptions.Parse(["--strategy", "ORB", "--symbols", "RELIANCE", "--from", "\u001b[31mred" + new string('x', 200), "--to", "2026-03-04"], Known);

        var error = parsed.Errors.Single(e => e.Contains("--from", StringComparison.Ordinal));
        error.ShouldNotContain("\u001b");
        error.Length.ShouldBeLessThan(200);
    }

    // The test host runs in globalization-invariant mode, so de-DE and tr-TR cannot be loaded: invariant clones with other separators stand in for them (as FillRulesTests does).
    private static CultureInfo Culture(string decimalSeparator, string groupSeparator)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = decimalSeparator;
        culture.NumberFormat.NumberGroupSeparator = groupSeparator;
        return culture;
    }

    [Theory]
    [InlineData(",", ".")]
    [InlineData(",", " ")]
    [InlineData(".", ",")]
    public void The_result_is_the_same_under_cultures_with_other_separators(string decimalSeparator, string groupSeparator)
    {
        var before = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = Culture(decimalSeparator, groupSeparator);

            var valid = Parse("--strategy ORB --symbols RELIANCE,TCS " + Window + " --param rr=2.5 --param or_min=15").Options.ShouldNotBeNull();
            var comma = Parse("--strategy ORB --symbols RELIANCE " + Window + " --param rr=2,5");
            var grouped = Parse("--strategy ORB --symbols RELIANCE " + Window + " --param rr=1.000,5");
            var dotted = Parse("--strategy ORB --symbols RELIANCE " + Window + " --param rr=1.000");

            valid.Parameters["rr"].ShouldBe(2.5);
            (valid.From, valid.To).ShouldBe((new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 4)));
            comma.Options.ShouldBeNull();
            grouped.Options.ShouldBeNull();
            dotted.Options.ShouldNotBeNull().Parameters["rr"].ShouldBe(1.0);
            Parse("--strategy ORB --symbols ISBN,INFY " + Window).Options.ShouldNotBeNull().Symbols.ShouldBe(["ISBN", "INFY"], "the symbols are not case-mapped, so the Turkish dotted and dotless i do not matter");
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = before;
        }
    }
}
