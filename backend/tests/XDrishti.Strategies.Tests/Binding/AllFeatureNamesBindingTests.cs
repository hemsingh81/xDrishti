using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;
using XDrishti.Strategies.Lint;
using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Binding;

/// <summary>Criterion 3: every one of the 56 catalog names can be written in its DSL form with default arguments and binds to the spec the catalog's <c>DefaultSpec</c> gives.</summary>
public sealed class AllFeatureNamesBindingTests
{
    /// <summary>The ten names that read the daily market context (written out; not taken from the catalog's flags).</summary>
    private static readonly string[] ContextNames = ["index_trend", "index_adx", "index_vol_pct", "vix", "vix_pct", "vix_chg", "breadth", "regime_trend", "regime_vol", "regime"];

    public static TheoryData<string> Names { get; } = [.. IndicatorCatalog.All.Select(i => i.Name)];

    public static TheoryData<string, Timeframe> NamesOnOtherTimeframes { get; } =
        [.. IndicatorCatalog.All.SelectMany(i => new[] { Timeframe.M5, Timeframe.M15, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1 }.Select(t => (i.Name, t)))];

    [Fact]
    public void The_table_of_forms_has_one_form_for_each_of_the_56_names_of_the_catalog()
    {
        IndicatorCatalog.All.Count.ShouldBe(56);
        AllFeatureForms.Forms.Count.ShouldBe(56);
        AllFeatureForms.Forms.Keys.OrderBy(n => n, StringComparer.Ordinal).ShouldBe(IndicatorCatalog.All.Select(i => i.Name).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void On_1m_every_form_binds_to_the_default_spec_of_the_catalog(string name)
    {
        IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();

        var plan = Compiling.Condition(AllFeatureForms.ConditionOf(name), Timeframe.M1);

        plan.Features.ShouldBe([info.DefaultSpec(Timeframe.M1)], name);
        plan.HistoryDepth.ShouldBe(1);
        plan.EvaluationTimeframe.ShouldBe(Timeframe.M1);
    }

    [Theory]
    [MemberData(nameof(NamesOnOtherTimeframes))]
    public void On_any_other_timeframe_a_form_binds_to_the_default_spec_there_or_is_refused_as_the_catalog_refuses_it(string name, Timeframe timeframe)
    {
        IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();

        // a date-driven feature changes once per date and is always read on 1m; every other feature is read on the strategy's timeframe
        var expected = info.DefaultSpec(info.DateDriven ? Timeframe.M1 : timeframe);
        var result = Compiling.Of(AllFeatureForms.ConditionOf(name), timeframe);

        if (!info.Supports(expected.Timeframe))
        {
            result.IsSuccess.ShouldBeFalse(name + " on " + timeframe);
            var error = result.Errors.ShouldHaveSingleItem();
            error.Code.ShouldBe(LintCodes.UnsupportedTimeframe);
            error.Message.ShouldContain("'" + name + "'", Case.Sensitive, "names the feature");
            error.Message.ShouldContain(timeframe.ToCode(), Case.Sensitive, "names the timeframe");
        }
        else if (FeatureFrame.Create([expected], new FrameOptions(Context: MarketContext.Create([]).Value, ExpiryDays: new HashSet<DateOnly>())).IsFailure)
        {
            // the default of a parameter can be refused on a longer timeframe (an opening range of 15 minutes is not a whole number of 30m or 1h bars)
            result.Errors.ShouldHaveSingleItem().Code.ShouldBe(LintCodes.BadArguments);
        }
        else
        {
            result.IsSuccess.ShouldBeTrue(name + " on " + timeframe + " -> " + Compiling.Describe(result));
            result.Condition!.Features.ShouldBe([expected]);
        }
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void The_plan_needs_a_context_for_exactly_the_ten_context_names_and_expiry_days_for_the_expiry_day(string name)
    {
        var plan = Compiling.Condition(AllFeatureForms.ConditionOf(name), Timeframe.M1);

        plan.NeedsContext.ShouldBe(ContextNames.Contains(name), name);
        plan.NeedsExpiryDays.ShouldBe(name == "is_expiry_day", name);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void A_form_written_with_empty_parentheses_binds_the_same_as_without_them_when_the_feature_has_no_parameters(string name)
    {
        IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();
        if (info.Parameters.Count > 0 || name == "regime")
        {
            return;
        }

        var bare = Compiling.Condition(name + " > 0", Timeframe.M1);
        var called = Compiling.Condition(name + "() > 0", Timeframe.M1);

        called.Features.ShouldBe(bare.Features, name);
    }
}
