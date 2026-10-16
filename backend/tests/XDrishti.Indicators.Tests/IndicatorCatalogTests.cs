using System.Collections.ObjectModel;
using System.Reflection;
using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests;

/// <summary>The catalog is complete and honest: every indicator class is listed, metadata is well-formed and the declared warm-up equals the measured first ready bar (criterion 12).</summary>
public sealed class IndicatorCatalogTests
{
    private static readonly List<Bar> Sessions = SeriesGenerator.Sessions(7, 60);

    /// <summary>1,100 gap-free sessions (about 220 weeks, 52 months): enough weekly and monthly bars for the longest warm-up of the test variants (42).</summary>
    private static readonly Lazy<List<Bar>> LongSessions = new(() => SeriesGenerator.Sessions(7, 1_100));

    private static readonly string[] IntradayOnlyNames = ["vwap", "or_high", "or_low"];

    /// <summary>The names that are tied to the trading session of one India date and so are refused on weekly and monthly bars (independent of the catalog's own flags).</summary>
    private static readonly string[] SessionBound =
    [
        "vwap", "or_high", "or_low", "prev_day_open", "prev_day_high", "prev_day_low", "prev_day_close", "prev_day_volume", "today_open", "today_high", "today_low", "gap_pct",
    ];

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
            // 1W and 1M: the first ready bar is measured in weeks and months, so the series is years long (cheap: the builders run at over 100 million minute bars per second)
            var sessionBars = timeframe == Timeframe.M1 ? Sessions : Closed(timeframe);
            if (timeframe is Timeframe.W1 or Timeframe.Mo1)
            {
                sessionBars.Count.ShouldBeGreaterThan(timeframe == Timeframe.W1 ? 200 : 45, timeframe.ToCode() + ": enough closed bars for the longest warm-up (42)");
            }

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
    public void The_catalog_lists_the_forty_four_names_of_p3_01_and_p3_01b_and_the_twelve_date_driven_ones_of_p3_01c()
    {
        string[] added =
        [
            "adx", "plus_di", "minus_di", "macd_line", "macd_signal", "macd_hist", "bb_upper", "bb_middle", "bb_lower", "bb_width", "supertrend", "supertrend_dir", "roc",
            "inside_bar", "nr", "engulfing", "pivot_high", "pivot_low", "pivot_high_age", "pivot_low_age", "open", "high", "low", "close", "volume",
        ];

        IndicatorCatalog.All.Count(i => !i.DateDriven).ShouldBe(19 + added.Length);
        IndicatorCatalog.All.Count(i => !i.DateDriven).ShouldBe(44);
        IndicatorCatalog.All.Count.ShouldBe(44 + TestSpecs.ContextNames.Count);
        IndicatorCatalog.All.Count.ShouldBe(56);
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
        // the date-driven names need the context and the expiry dates in the options; an empty context and an empty set are enough for the frame to be valid
        var options = new FrameOptions(Context: MarketContext.Create([]).Value, ExpiryDays: new HashSet<DateOnly>());
        foreach (var info in IndicatorCatalog.All.Where(i => !i.Name.StartsWith("or_", StringComparison.Ordinal)))
        {
            FeatureFrame.Create([info.DefaultSpec()], options).IsSuccess.ShouldBeTrue(info.Name);
            foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
            {
                FeatureFrame.Create([info.DefaultSpec(timeframe)], options).IsSuccess.ShouldBe(info.Supports(timeframe), $"{info.Name} on {timeframe.ToCode()}");
            }
        }
    }

    [Fact]
    public void The_supported_timeframes_are_the_nine_stored_ones_shortest_first()
    {
        IndicatorCatalog.SupportedTimeframes.ShouldBe([Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1, Timeframe.W1, Timeframe.Mo1]);
        IndicatorCatalog.SupportedTimeframes.ShouldBe(Timeframes.All);
    }

    [Fact]
    public void Weekly_and_monthly_bars_take_every_continuous_indicator_and_refuse_the_session_bound_ones()
    {
        foreach (var timeframe in new[] { Timeframe.W1, Timeframe.Mo1 })
        {
            var refused = IndicatorCatalog.All.Where(i => !i.Supports(timeframe)).Select(i => i.Name).ToList();
            var accepted = IndicatorCatalog.All.Where(i => i.Supports(timeframe)).ToList();

            refused.Order().ShouldBe(SessionBound.Concat(TestSpecs.ContextNames).Order(), timeframe.ToCode() + ": the session-bound ones and the date-driven ones (1m only)");
            accepted.Count.ShouldBe(44 - SessionBound.Length, timeframe.ToCode());
            accepted.ShouldAllBe(i => i.SessionBehaviour == SessionBehaviour.Continuous && !i.IntradayOnly && !i.DateDriven);
        }
    }

    [Fact]
    public void Daily_bars_refuse_the_intraday_indicators_and_the_date_driven_ones_and_the_intraday_timeframes_refuse_only_the_date_driven_ones()
    {
        IndicatorCatalog.All.Where(i => !i.Supports(Timeframe.D1)).Select(i => i.Name).Order().ShouldBe(IntradayOnlyNames.Concat(TestSpecs.ContextNames).Order());
        IndicatorCatalog.All.ShouldAllBe(i => i.Supports(Timeframe.M1), "1m takes everything");
        foreach (var timeframe in new[] { Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1 })
        {
            IndicatorCatalog.All.Where(i => !i.Supports(timeframe)).Select(i => i.Name).Order().ShouldBe(TestSpecs.ContextNames.Order(), timeframe.ToCode());
        }

        IndicatorCatalog.All.ShouldAllBe(i => !i.Supports((Timeframe)99) && !i.Supports((Timeframe)(-1)));
    }

    [Fact]
    public void Each_indicator_lists_the_supported_timeframes_it_can_run_on_shortest_first_in_a_read_only_view()
    {
        foreach (var info in IndicatorCatalog.All)
        {
            info.SupportedTimeframes.ShouldBe(IndicatorCatalog.SupportedTimeframes.Where(info.Supports), info.Name);
            info.SupportedTimeframes.ShouldBeAssignableTo<ReadOnlyCollection<Timeframe>>();
            info.SupportedTimeframes.ShouldBeSameAs(info.SupportedTimeframes, "built once");
            info.SupportedTimeframes[0].ShouldBe(Timeframe.M1, info.Name);
        }

        IndicatorCatalog.TryGet("sma", out var sma).ShouldBeTrue();
        IndicatorCatalog.TryGet("vwap", out var vwap).ShouldBeTrue();
        IndicatorCatalog.TryGet("or_low", out var orLow).ShouldBeTrue();
        IndicatorCatalog.TryGet("prev_day_close", out var previousClose).ShouldBeTrue();
        IndicatorCatalog.TryGet("macd_hist", out var macd).ShouldBeTrue();
        sma.SupportedTimeframes.ShouldBe(Timeframes.All);
        macd.SupportedTimeframes.ShouldBe(Timeframes.All);
        vwap.SupportedTimeframes.ShouldBe(Timeframes.Intraday.Prepend(Timeframe.M1), "1m and the intraday derived ones; not 1D, 1W or 1M");
        orLow.SupportedTimeframes.ShouldBe(vwap.SupportedTimeframes);
        previousClose.SupportedTimeframes.ShouldBe(Timeframes.Intraday.Prepend(Timeframe.M1).Append(Timeframe.D1), "the session values are read on 1m, the intraday timeframes and 1D");
        Should.Throw<NotSupportedException>(() => ((IList<Timeframe>)sma.SupportedTimeframes)[0] = Timeframe.D1);
    }

    [Fact]
    public void An_indicator_info_has_no_state_that_a_read_can_change_so_the_shared_catalog_is_immutable()
    {
        // The catalog is a process-wide static array: a field filled on first use (a lazily built list) would be shared mutable state raced by two threads (review of P3-01c, M20).
        var writable = typeof(IndicatorInfo).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(f => !f.IsInitOnly).Select(f => f.Name);

        writable.ShouldBeEmpty("every field of IndicatorInfo is readonly");
        typeof(IndicatorInfo).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(p => p.SetMethod is { IsPublic: true }).ShouldBeEmpty();
    }

    [Fact]
    public void Many_threads_reading_the_supported_timeframes_of_a_new_indicator_info_at_once_all_get_the_one_list()
    {
        const int Threads = 16;
        var info = new IndicatorInfo("probe", "a probe", [], SessionBehaviour.Continuous, false, PriceSource.Close, false, _ => 1, _ => new Sma(1));
        var lists = new IReadOnlyList<Timeframe>[Threads];
        using var barrier = new Barrier(Threads);

        Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, i =>
        {
            barrier.SignalAndWait();
            lists[i] = info.SupportedTimeframes;
        });

        lists.ShouldAllBe(l => ReferenceEquals(l, lists[0]));
        lists[0].ShouldBe(Timeframes.All);
    }

    [Theory]
    [InlineData(Timeframe.W1, "1W")]
    [InlineData(Timeframe.Mo1, "1M")]
    public void A_session_bound_indicator_on_a_weekly_or_monthly_bar_is_refused_naming_the_feature_and_the_timeframe(Timeframe timeframe, string code)
    {
        foreach (var name in SessionBound)
        {
            var spec = new FeatureSpec(name, timeframe, PriceSource.Close, name.StartsWith("or_", StringComparison.Ordinal) ? 30 : 0);

            var result = FeatureFrame.Create([new FeatureSpec("sma", Timeframe.M1, PriceSource.Close, 3), spec]);

            result.IsFailure.ShouldBeTrue($"{name} on {code}");
            var field = result.Error.Fields.ShouldNotBeNull().Single();
            field.Field.ShouldBe("specs[1].timeframe", $"{name} on {code}");
            field.Message.ShouldContain($"'{name}'");
            field.Message.ShouldContain(code);
        }
    }

    [Fact]
    public void The_refusal_on_daily_bars_keeps_its_message_and_the_one_on_weekly_bars_says_where_to_read_the_value()
    {
        string Message(string name, Timeframe timeframe) => FeatureFrame.Create([new FeatureSpec(name, timeframe, PriceSource.Close, 0)]).Error.Fields.ShouldNotBeNull().Single().Message;

        Message("vwap", Timeframe.D1).ShouldBe("'vwap' needs intraday bars; it cannot be computed on 1D.");
        Message("vwap", Timeframe.W1).ShouldBe("'vwap' needs intraday bars; it cannot be computed on 1W.");
        Message("prev_day_close", Timeframe.Mo1).ShouldBe("'prev_day_close' is a value of the trading session; it cannot be computed on 1M (read it on 1m or 1D).");
        Message("gap_pct", Timeframe.W1).ShouldBe("'gap_pct' is a value of the trading session; it cannot be computed on 1W (read it on 1m or 1D).");
        FeatureFrame.Create([new FeatureSpec("prev_day_close", Timeframe.D1)]).IsSuccess.ShouldBeTrue("the session values are still read on 1D");
    }

    [Fact]
    public void The_warm_up_on_weekly_and_monthly_bars_is_counted_in_bars_of_that_timeframe()
    {
        int Warm(string name, Timeframe timeframe, double p1 = 0, double p2 = 0, double p3 = 0) =>
            IndicatorCatalog.TryGet(name, out var info) ? info.WarmUp(new FeatureSpec(name, timeframe, PriceSource.Close, p1, p2, p3)) : -1;

        Warm("ema", Timeframe.W1, 10).ShouldBe(10, "ema(10) on 1W needs ten weekly bars");
        Warm("ema", Timeframe.Mo1, 10).ShouldBe(10);
        Warm("sma", Timeframe.W1, 20).ShouldBe(20);
        Warm("rsi", Timeframe.Mo1, 14).ShouldBe(15);
        Warm("adx", Timeframe.W1, 14).ShouldBe(28);
        Warm("macd_signal", Timeframe.Mo1, 12, 26, 9).ShouldBe(34);
        Warm("pivot_high", Timeframe.W1, 5).ShouldBe(11);
        Warm("close", Timeframe.W1).ShouldBe(1);
        FeatureFrame.Create([new FeatureSpec("ema", Timeframe.W1, PriceSource.Close, 10)]).Value.WarmUp(0).ShouldBe(10);
    }

    [Fact]
    public void The_declared_warm_up_of_the_date_driven_names_is_the_measured_number_of_context_days_for_the_default_options()
    {
        foreach (var name in TestSpecs.ContextNames)
        {
            IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();

            var (measured, instance) = MeasureDateFeature(name, new ContextOptions(), 330);

            measured.ShouldBe(info.WarmUp(info.DefaultSpec()), $"{name}: the declared warm-up");
            instance.ShouldBe(measured, $"{name}: the warm-up of the instance in a frame");
        }
    }

    [Theory]
    [MemberData(nameof(OptionSets))]
    public void The_warm_up_of_a_date_driven_instance_is_the_measured_number_of_context_days_for_the_options_of_its_context(string label, ContextOptions options, int days)
    {
        foreach (var name in TestSpecs.ContextNames)
        {
            var (measured, instance) = MeasureDateFeature(name, options, days);

            instance.ShouldBe(measured, $"{label}: {name}");
        }
    }

    public static TheoryData<string, ContextOptions, int> OptionSets() => new()
    {
        { "small", FrameContextFixtures.Small, 60 },
        { "slow trend", FrameContextFixtures.SlowTrend, 80 },
        { "long adx", new ContextOptions(TrendSmaPeriod: 5, TrendSlopeDays: 1, AdxPeriod: 20, AtrPeriod: 2, PercentileWindow: 3), 80 },
        { "one-day slope and window", new ContextOptions(TrendSmaPeriod: 2, TrendSlopeDays: 1, AdxPeriod: 1, AtrPeriod: 1, PercentileWindow: 1), 30 },
    };

    [Fact]
    public void The_declared_warm_ups_of_the_twelve_names_follow_the_definitions()
    {
        int Warm(string name) => IndicatorCatalog.TryGet(name, out var info) ? info.WarmUp(info.DefaultSpec()) : -1;

        Warm("day_of_week").ShouldBe(1);
        Warm("is_expiry_day").ShouldBe(1);
        Warm("index_trend").ShouldBe(55, "the 50-day average, a 5-day slope of it");
        Warm("index_adx").ShouldBe(28, "ADX(14) is ready at 2 * 14");
        Warm("index_vol_pct").ShouldBe(265, "the ATR(14) and then a window of 252 of its values: 14 + 252 - 1");
        Warm("vix").ShouldBe(1);
        Warm("vix_pct").ShouldBe(252);
        Warm("vix_chg").ShouldBe(2);
        Warm("breadth").ShouldBe(1);
        Warm("regime_trend").ShouldBe(55);
        Warm("regime_vol").ShouldBe(265);
        Warm("regime").ShouldBe(265);
    }

    /// <summary>
    /// For a date-driven feature on a context with a VIX and a breadth on every day, stepped once on the date of each record (so that the date of record i sees record i - 1): how many context days preceded the first date
    /// at which it was ready (the number of dates stepped, for the two calendar names, which read no context), after checking that it stays ready, and the warm-up the frame reports for it.
    /// </summary>
    private static (int Measured, int Instance) MeasureDateFeature(string name, ContextOptions options, int dayCount)
    {
        var records = FrameContextFixtures.Consecutive(40, dayCount);
        var context = FrameContextFixtures.Build(records, options);
        var frame = FeatureFrame.Create([new FeatureSpec(name)], new FrameOptions(Context: context, ExpiryDays: new HashSet<DateOnly>())).Value;
        var first = -1;
        for (var i = 0; i < records.Count; i++)
        {
            frame.Step(Minutes.Close(records[i].Date, Minutes.Open, 100));

            if (frame.IsReady(0))
            {
                first = first < 0 ? i : first;
            }
            else
            {
                first.ShouldBe(-1, $"{name} stays ready once it is ready");
            }
        }

        first.ShouldBeGreaterThanOrEqualTo(0, $"{name} was ready on a context of {dayCount} days");
        return (name is "day_of_week" or "is_expiry_day" ? first + 1 : first, frame.WarmUp(0));
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

    /// <summary>The closed bars the builder emits for the gap-free series (the long one for the weekly and monthly timeframes).</summary>
    private static List<Bar> Closed(Timeframe timeframe)
    {
        var builder = new TimeframeBuilder(timeframe);
        var bars = new List<Bar>();
        foreach (var bar in timeframe is Timeframe.W1 or Timeframe.Mo1 ? LongSessions.Value : Sessions)
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
