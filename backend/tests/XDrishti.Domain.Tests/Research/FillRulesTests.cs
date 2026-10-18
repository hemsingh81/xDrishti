using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

public class FillRulesTests
{
    private const string DefaultJson = """{"entry_limit_percent":0.1,"slippage":{"ticks":1,"bar_range_frac":0.05,"stop_exit_extra_ticks":2,"gap_fill":"at_open"}}""";

    [Fact]
    public void Default_is_a_tenth_of_a_percent_with_the_default_slippage()
    {
        FillRules.Default.EntryLimitPercent.ShouldBe(0.1);
        FillRules.Default.Slippage.ShouldBe(SlippageConfig.Default);
    }

    [Fact]
    public void Basis_is_modelled_for_the_default_slippage_and_exact_when_there_is_none()
    {
        FillRules.Default.Basis.ShouldBe(FillBasis.Modelled);
        FillRules.Create(0.1, SlippageConfig.None).Value.Basis.ShouldBe(FillBasis.Exact);
        FillRules.Create(0.1, SlippageConfig.Create(0, 0.01, 0).Value).Value.Basis.ShouldBe(FillBasis.Modelled);
    }

    [Fact]
    public void Default_canonical_json_is_exact()
    {
        FillRules.Default.ToCanonicalJson().ShouldBe(DefaultJson);
    }

    [Fact]
    public void Canonical_json_does_not_depend_on_the_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CommaCulture();

            FillRules.Default.ToCanonicalJson().ShouldBe(DefaultJson);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Canonical_json_writes_other_values_in_shortest_form()
    {
        var rules = FillRules.Create(0.25, SlippageConfig.None).Value;

        rules.ToCanonicalJson().ShouldBe("""{"entry_limit_percent":0.25,"slippage":{"ticks":0,"bar_range_frac":0,"stop_exit_extra_ticks":0,"gap_fill":"at_open"}}""");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Create_accepts_the_bounds(double percent)
    {
        FillRules.Create(percent, SlippageConfig.Default).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-0.01)]
    [InlineData(5.01)]
    public void Create_refuses_a_bad_percent(double percent)
    {
        var result = FillRules.Create(percent, SlippageConfig.Default);

        result.Error.Code.ShouldBe("simulation.fill_rules_invalid");
        result.Error.Message.ShouldContain("EntryLimitPercent");
    }

    [Fact]
    public void Create_refuses_a_null_slippage()
    {
        var result = FillRules.Create(0.1, null!);

        result.Error.Code.ShouldBe("simulation.fill_rules_invalid");
        result.Error.Message.ShouldContain("Slippage");
    }

    [Fact]
    public void Create_turns_a_negative_zero_limit_percent_into_zero_so_the_canonical_json_never_says_minus_zero()
    {
        var rules = FillRules.Create(-0.0, SlippageConfig.None).Value;

        double.IsNegative(rules.EntryLimitPercent).ShouldBeFalse();
        rules.ToCanonicalJson().ShouldBe("{\"entry_limit_percent\":0,\"slippage\":{\"ticks\":0,\"bar_range_frac\":0,\"stop_exit_extra_ticks\":0,\"gap_fill\":\"at_open\"}}");
    }

    [Fact]
    public void Only_Create_can_build_one_and_nothing_can_be_changed()
    {
        typeof(FillRules).GetConstructors().ShouldBeEmpty();
        typeof(FillRules).GetProperties().ShouldAllBe(p => p.SetMethod == null);
    }

    // The test host runs in globalization-invariant mode, so de-DE cannot be loaded: an invariant clone with German separators stands in for it.
    private static CultureInfo CommaCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        return culture;
    }
}
