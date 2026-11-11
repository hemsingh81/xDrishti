namespace XDrishti.Indicators.Tests;

/// <summary>
/// <see cref="IndicatorInfo.RequiresContext"/> and <see cref="IndicatorInfo.RequiresExpiryDays"/> (P3-02, item 0): the catalog says which names need a market context or expiry dates, so that the rules language does
/// not have to know. The ten names that read the context and the one that reads the expiry dates are written out here, independently of the catalog's own code, and pinned for all 56 names; the frame must agree
/// with the flags for every name.
/// </summary>
public sealed class IndicatorInfoRequirementsTests
{
    /// <summary>The ten names that read the daily market context (written out; not derived from the catalog).</summary>
    private static readonly string[] ReadContext = ["index_trend", "index_adx", "index_vol_pct", "vix", "vix_pct", "vix_chg", "breadth", "regime_trend", "regime_vol", "regime"];

    private const string ExpiryName = "is_expiry_day";

    public static TheoryData<string> AllNames { get; } = [.. IndicatorCatalog.All.Select(i => i.Name)];

    private static IndicatorInfo Info(string name)
    {
        IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue(name);
        return info;
    }

    private static FrameOptions Both() => new(Context: MarketContext.Create([]).Value, ExpiryDays: new HashSet<DateOnly>(), ResultsDays: new Dictionary<DateOnly, ResultsDayValues>(), RelativeStrength: RelativeStrengthDays.Empty);

    [Fact]
    public void There_are_sixty_names_and_the_ten_context_names_are_among_them_as_date_driven_ones()
    {
        IndicatorCatalog.All.Count.ShouldBe(64);
        ReadContext.Distinct().Count().ShouldBe(10);
        foreach (var name in ReadContext.Append(ExpiryName))
        {
            Info(name).DateDriven.ShouldBeTrue(name);
        }
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Only_the_ten_names_that_read_the_market_context_require_it(string name)
    {
        Info(name).RequiresContext.ShouldBe(ReadContext.Contains(name), name);
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void Only_the_expiry_day_requires_the_expiry_dates(string name)
    {
        Info(name).RequiresExpiryDays.ShouldBe(name == ExpiryName, name);
    }

    [Fact]
    public void The_flags_are_set_for_exactly_ten_names_and_one_name_and_never_for_the_weekday_or_a_bar_feature()
    {
        IndicatorCatalog.All.Where(i => i.RequiresContext).Select(i => i.Name).ShouldBe(ReadContext);
        IndicatorCatalog.All.Where(i => i.RequiresExpiryDays).Select(i => i.Name).ShouldBe([ExpiryName]);
        IndicatorCatalog.All.Where(i => i.RequiresContext || i.RequiresExpiryDays).ShouldAllBe(i => i.DateDriven, "only date-driven features need frame options");
        Info("day_of_week").RequiresContext.ShouldBeFalse();
        Info("day_of_week").RequiresExpiryDays.ShouldBeFalse();
        Info(ExpiryName).RequiresContext.ShouldBeFalse("the expiry day reads the date and the given dates, not the market context");
        Info("regime").RequiresExpiryDays.ShouldBeFalse();
    }

    [Fact]
    public void Exactly_the_four_results_names_require_the_results_days_and_nothing_else_does()
    {
        IndicatorCatalog.All.Where(i => i.RequiresResultsDays).Select(i => i.Name).ShouldBe(["days_to_results", "days_since_results", "results_sue_q", "results_car3_q"]);
        IndicatorCatalog.All.Where(i => i.RequiresResultsDays).ShouldAllBe(i => i.DateDriven && !i.RequiresContext && !i.RequiresExpiryDays);
    }

    [Fact]
    public void Exactly_the_three_relative_strength_names_require_the_stored_rows_and_nothing_else_does()
    {
        IndicatorCatalog.All.Where(i => i.RequiresRelativeStrength).Select(i => i.Name).ShouldBe(["mom_rank", "rs_vs_index", "sector_rs"]);
        IndicatorCatalog.All.Where(i => i.RequiresRelativeStrength).ShouldAllBe(i => i.DateDriven && !i.RequiresContext && !i.RequiresExpiryDays && !i.RequiresResultsDays);
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void A_frame_asked_for_the_feature_without_options_agrees_with_the_flags(string name)
    {
        var info = Info(name);

        var result = FeatureFrame.Create([info.DefaultSpec()]);

        if (info.RequiresContext)
        {
            result.IsFailure.ShouldBeTrue(name);
            var field = result.Error.Fields.ShouldNotBeNull().Single();
            field.Field.ShouldBe("options.context", name);
            field.Message.ShouldBe($"The market context is needed by '{name}'; give FrameOptions.Context.");
        }
        else if (info.RequiresExpiryDays)
        {
            result.IsFailure.ShouldBeTrue(name);
            var field = result.Error.Fields.ShouldNotBeNull().Single();
            field.Field.ShouldBe("options.expiryDays", name);
            field.Message.ShouldBe($"The expiry dates are needed by '{name}'; give FrameOptions.ExpiryDays (an empty set is allowed).");
        }
        else if (info.RequiresResultsDays)
        {
            result.IsFailure.ShouldBeTrue(name);
            var field = result.Error.Fields.ShouldNotBeNull().Single();
            field.Field.ShouldBe("options.resultsDays", name);
            field.Message.ShouldBe($"The results-event days are needed by '{name}'; give FrameOptions.ResultsDays (an empty series is allowed).");
        }
        else if (info.RequiresRelativeStrength)
        {
            result.IsFailure.ShouldBeTrue(name);
            var field = result.Error.Fields.ShouldNotBeNull().Single();
            field.Field.ShouldBe("options.relativeStrength", name);
            field.Message.ShouldBe($"The stored relative-strength rows are needed by '{name}'; give FrameOptions.RelativeStrength (an empty set is allowed).");
        }
        else
        {
            result.IsSuccess.ShouldBeTrue(name);
        }
    }

    [Theory]
    [MemberData(nameof(AllNames))]
    public void A_frame_given_exactly_what_the_flags_ask_for_is_valid_and_the_other_option_is_no_substitute(string name)
    {
        var info = Info(name);
        var context = new FrameOptions(Context: MarketContext.Create([]).Value);
        var expiryDays = new FrameOptions(ExpiryDays: new HashSet<DateOnly>());
        var resultsDays = new FrameOptions(ResultsDays: new Dictionary<DateOnly, ResultsDayValues>());
        var relativeStrength = new FrameOptions(RelativeStrength: RelativeStrengthDays.Empty);

        FeatureFrame.Create([info.DefaultSpec()], Both()).IsSuccess.ShouldBeTrue(name);
        FeatureFrame.Create([info.DefaultSpec()], context).IsSuccess.ShouldBe(!info.RequiresExpiryDays && !info.RequiresResultsDays && !info.RequiresRelativeStrength, name + " with the context only");
        FeatureFrame.Create([info.DefaultSpec()], expiryDays).IsSuccess.ShouldBe(!info.RequiresContext && !info.RequiresResultsDays && !info.RequiresRelativeStrength, name + " with the expiry dates only");
        FeatureFrame.Create([info.DefaultSpec()], resultsDays).IsSuccess.ShouldBe(!info.RequiresContext && !info.RequiresExpiryDays && !info.RequiresRelativeStrength, name + " with the results days only");
        FeatureFrame.Create([info.DefaultSpec()], relativeStrength).IsSuccess.ShouldBe(!info.RequiresContext && !info.RequiresExpiryDays && !info.RequiresResultsDays, name + " with the relative strength only");
    }
}
