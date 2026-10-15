using System.Collections.ObjectModel;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>The catalog is complete and honest: every indicator class is listed, metadata is well-formed and the declared warm-up equals the measured first ready bar (criterion 12).</summary>
public sealed class IndicatorCatalogTests
{
    private static readonly List<Bar> Sessions = SeriesGenerator.Sessions(7, 60);

    [Fact]
    public void Every_public_indicator_class_is_produced_by_a_catalog_entry_and_every_entry_by_an_indicator_class()
    {
        var classes = typeof(IIndicator).Assembly.GetTypes().Where(t => t.IsPublic && t.IsClass && !t.IsAbstract && typeof(IIndicator).IsAssignableFrom(t)).ToHashSet();
        var produced = IndicatorCatalog.All.Select(i => i.Create(i.DefaultSpec(Timeframe.M1) with { P1 = i.Name.StartsWith("or_", StringComparison.Ordinal) ? 15 : i.DefaultSpec().P1 }).GetType()).ToHashSet();

        classes.ShouldNotBeEmpty();
        classes.Except(produced).ShouldBeEmpty("an indicator class without catalog metadata");
        produced.Except(classes).ShouldBeEmpty();
    }

    [Fact]
    public void The_session_context_has_one_catalog_entry_per_field()
    {
        var names = IndicatorCatalog.All.Where(i => i.Create(i.DefaultSpec()) is SessionContext).Select(i => i.Name).ToList();

        names.Count.ShouldBe(Enum.GetValues<SessionField>().Length);
    }

    [Fact]
    public void Names_are_unique_lower_snake_case_and_parameters_are_well_formed_with_at_most_four()
    {
        IndicatorCatalog.All.Select(i => i.Name).Distinct().Count().ShouldBe(IndicatorCatalog.All.Count);
        foreach (var info in IndicatorCatalog.All)
        {
            info.Name.ShouldMatch("^[a-z][a-z0-9_]*$");
            info.Summary.ShouldNotBeNullOrWhiteSpace();
            info.Parameters.Count.ShouldBeLessThanOrEqualTo(4);
            foreach (var parameter in info.Parameters)
            {
                parameter.Name.ShouldNotBeNullOrWhiteSpace();
                parameter.Min.ShouldBeLessThanOrEqualTo(parameter.Default);
                parameter.Default.ShouldBeLessThanOrEqualTo(parameter.Max);
                if (parameter.Kind == ParameterKind.Whole)
                {
                    parameter.Default.ShouldBe(Math.Floor(parameter.Default));
                }
            }

            IndicatorCatalog.TryGet(info.Name, out var found).ShouldBeTrue();
            found.ShouldBeSameAs(info);
        }
    }

    [Fact]
    public void Rolling_indicators_are_continuous_and_session_indicators_reset_per_session()
    {
        foreach (var name in new[] { "sma", "ema", "rsi", "atr", "highest", "lowest", "rel_volume", "adx", "macd_signal", "bb_upper", "supertrend", "roc", "nr", "pivot_high", "close" })
        {
            IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();
            info.SessionBehaviour.ShouldBe(SessionBehaviour.Continuous, name);
        }

        foreach (var name in new[] { "vwap", "or_high", "or_low", "prev_day_close", "today_open", "gap_pct" })
        {
            IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();
            info.SessionBehaviour.ShouldBe(SessionBehaviour.PerSession, name);
        }
    }

    [Fact]
    public void An_unknown_name_is_not_found()
    {
        IndicatorCatalog.TryGet("macd", out _).ShouldBeFalse();
        IndicatorCatalog.TryGet("bb", out _).ShouldBeFalse();
        IndicatorCatalog.TryGet(null, out _).ShouldBeFalse();
        IndicatorCatalog.TryGet("SMA", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_declared_warm_up_is_the_measured_first_ready_bar_on_every_timeframe()
    {
        foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
        {
            var sessionBars = timeframe == Timeframe.M1 ? Sessions : Closed(timeframe);
            foreach (var spec in TestSpecs.Variants(timeframe))
            {
                IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
                var indicator = info.Create(spec);

                // A pivot is ready from the first pivot on, which depends on the data: they are measured on a series whose first pivot is the earliest possible one.
                var fed = spec.Name.StartsWith("pivot_", StringComparison.Ordinal) ? PivotShape((int)spec.P1) : sessionBars;
                var first = 0;
                for (var i = 0; i < fed.Count && first == 0; i++)
                {
                    indicator.Update(fed[i]);
                    if (indicator.IsReady)
                    {
                        first = i + 1;
                    }
                }

                first.ShouldBe(info.WarmUp(spec), TestSpecs.Describe(spec));
                indicator.WarmUp.ShouldBe(info.WarmUp(spec), TestSpecs.Describe(spec));
            }
        }
    }

    [Fact]
    public void A_pivot_is_ready_no_earlier_than_declared_on_a_random_series_and_is_a_number_from_then_on()
    {
        foreach (var spec in TestSpecs.Variants(Timeframe.M1).Where(s => s.Name.StartsWith("pivot_", StringComparison.Ordinal)))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var indicator = info.Create(spec);
            var first = 0;
            for (var i = 0; i < Sessions.Count; i++)
            {
                indicator.Update(Sessions[i]);
                if (indicator.IsReady && first == 0)
                {
                    first = i + 1;
                }

                if (first > 0)
                {
                    indicator.IsReady.ShouldBeTrue(TestSpecs.Describe(spec));
                }
            }

            first.ShouldBeGreaterThanOrEqualTo(info.WarmUp(spec), TestSpecs.Describe(spec));
        }
    }

    [Fact]
    public void The_catalog_lists_the_forty_four_names_of_p3_01_and_p3_01b()
    {
        string[] added =
        [
            "adx", "plus_di", "minus_di", "macd_line", "macd_signal", "macd_hist", "bb_upper", "bb_middle", "bb_lower", "bb_width", "supertrend", "supertrend_dir", "roc",
            "inside_bar", "nr", "engulfing", "pivot_high", "pivot_low", "pivot_high_age", "pivot_low_age", "open", "high", "low", "close", "volume",
        ];

        IndicatorCatalog.All.Count.ShouldBe(19 + added.Length);
        IndicatorCatalog.All.Count.ShouldBe(44);
        foreach (var name in added)
        {
            IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue(name);
            info.SessionBehaviour.ShouldBe(SessionBehaviour.Continuous, name);
            info.Summary.ShouldNotBeNullOrWhiteSpace();
            info.Parameters.Count.ShouldBeLessThanOrEqualTo(4);
            FeatureFrame.Create([info.DefaultSpec()]).IsSuccess.ShouldBeTrue(name);
        }
    }

    [Theory]
    [InlineData("adx", 1, 14, 0, 0)]
    [InlineData("plus_di", 1, 14, 0, 0)]
    [InlineData("minus_di", 1, 14, 0, 0)]
    [InlineData("macd_line", 1, 12, 26, 9)]
    [InlineData("macd_signal", 1, 12, 26, 9)]
    [InlineData("macd_hist", 1, 12, 26, 9)]
    [InlineData("bb_upper", 1, 20, 2, 0)]
    [InlineData("bb_width", 1, 20, 2, 0)]
    [InlineData("supertrend", 1, 10, 3, 0)]
    [InlineData("supertrend_dir", 1, 10, 3, 0)]
    [InlineData("roc", 1, 10, 0, 0)]
    [InlineData("nr", 2, 7, 0, 0)]
    [InlineData("pivot_high", 1, 5, 0, 0)]
    [InlineData("pivot_low_age", 1, 5, 0, 0)]
    public void The_new_entries_have_the_documented_parameter_ranges_and_defaults(string name, int firstMin, double d1, double d2, double d3)
    {
        IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();

        var defaults = info.DefaultSpec();
        info.Parameters[0].Min.ShouldBe(firstMin);
        (defaults.P1, defaults.P2, defaults.P3).ShouldBe((d1, d2, d3));
        defaults.P4.ShouldBe(0);
    }

    [Fact]
    public void The_new_warm_ups_follow_the_definitions()
    {
        int Warm(string name, double p1 = 0, double p2 = 0, double p3 = 0) => IndicatorCatalog.TryGet(name, out var info) ? info.WarmUp(new FeatureSpec(name, Timeframe.M1, PriceSource.Close, p1, p2, p3)) : -1;

        Warm("adx", 14).ShouldBe(28);
        Warm("plus_di", 14).ShouldBe(15);
        Warm("minus_di", 14).ShouldBe(15);
        Warm("macd_line", 12, 26, 9).ShouldBe(26);
        Warm("macd_signal", 12, 26, 9).ShouldBe(34);
        Warm("macd_hist", 12, 26, 9).ShouldBe(34);
        Warm("bb_upper", 20, 2).ShouldBe(20);
        Warm("bb_middle", 20, 2).ShouldBe(20);
        Warm("supertrend", 10, 3).ShouldBe(10);
        Warm("supertrend_dir", 10, 3).ShouldBe(10);
        Warm("roc", 10).ShouldBe(11);
        Warm("inside_bar").ShouldBe(2);
        Warm("engulfing").ShouldBe(2);
        Warm("nr", 7).ShouldBe(7);
        Warm("pivot_high", 5).ShouldBe(11);
        Warm("pivot_low_age", 5).ShouldBe(11);
        Warm("close").ShouldBe(1);
    }

    [Fact]
    public void The_catalog_collections_are_read_only_and_cannot_be_cast_back_to_the_array_behind_them()
    {
        IndicatorCatalog.All.ShouldNotBeOfType<IndicatorInfo[]>();
        IndicatorCatalog.SupportedTimeframes.ShouldNotBeOfType<Timeframe[]>();
        IndicatorCatalog.All.ShouldBeAssignableTo<ReadOnlyCollection<IndicatorInfo>>();
        IndicatorCatalog.SupportedTimeframes.ShouldBeAssignableTo<ReadOnlyCollection<Timeframe>>();
        IndicatorCatalog.TryGet("sma", out var sma).ShouldBeTrue();
        sma.Parameters.ShouldBeAssignableTo<ReadOnlyCollection<IndicatorParameter>>();

        Should.Throw<NotSupportedException>(() => ((IList<IndicatorInfo>)IndicatorCatalog.All)[0] = null!);
        Should.Throw<NotSupportedException>(() => ((IList<Timeframe>)IndicatorCatalog.SupportedTimeframes)[0] = Timeframe.D1);
        Should.Throw<NotSupportedException>(() => ((IList<IndicatorParameter>)sma.Parameters)[0] = null!);
        IndicatorCatalog.SupportedTimeframes[0].ShouldBe(Timeframe.M1);
    }

    [Fact]
    public void A_not_ready_indicator_reports_nan_and_a_ready_one_a_number()
    {
        foreach (var spec in TestSpecs.Variants(Timeframe.M1))
        {
            IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
            var indicator = info.Create(spec);
            indicator.IsReady.ShouldBeFalse(TestSpecs.Describe(spec));
            indicator.Value.ShouldBeNaN();
            foreach (var bar in Sessions.Take(800))
            {
                indicator.Update(bar);
                indicator.IsReady.ShouldBe(!double.IsNaN(indicator.Value), TestSpecs.Describe(spec));
            }
        }
    }

    [Fact]
    public void Default_specs_of_the_catalog_are_valid_where_the_timeframe_allows()
    {
        foreach (var info in IndicatorCatalog.All.Where(i => !i.Name.StartsWith("or_", StringComparison.Ordinal)))
        {
            FeatureFrame.Create([info.DefaultSpec()]).IsSuccess.ShouldBeTrue(info.Name);
        }
    }

    /// <summary>Bars whose highs rise to a peak at index n and fall again, and whose lows fall to a trough there: the first pivot (high and low) is confirmed at bar 2n + 1, the earliest possible.</summary>
    private static List<Bar> PivotShape(int n)
    {
        var bars = new List<Bar>();
        var day = new DateOnly(2026, 3, 2);
        for (var k = 0; k <= 2 * n + 2; k++)
        {
            var swing = k <= n ? k : 2 * n - k;
            bars.Add(Minutes.Bar(day, Minutes.Open + k, 100, 100 + swing, 100 - swing, 100));
        }

        return bars;
    }

    /// <summary>The closed bars the builder emits for the long gap-free series.</summary>
    private static List<Bar> Closed(Timeframe timeframe)
    {
        var builder = new TimeframeBuilder(timeframe);
        var bars = new List<Bar>();
        foreach (var bar in Sessions)
        {
            var count = builder.Push(bar, out var first, out var second);
            if (count > 0)
            {
                bars.Add(first.Bar);
            }

            if (count > 1)
            {
                bars.Add(second.Bar);
            }
        }

        return bars;
    }
}
