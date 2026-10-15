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
        foreach (var name in new[] { "sma", "ema", "rsi", "atr", "highest", "lowest", "rel_volume" })
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
        IndicatorCatalog.TryGet(null, out _).ShouldBeFalse();
        IndicatorCatalog.TryGet("SMA", out _).ShouldBeFalse();
    }

    [Fact]
    public void The_declared_warm_up_is_the_measured_first_ready_bar_on_every_timeframe()
    {
        foreach (var timeframe in IndicatorCatalog.SupportedTimeframes)
        {
            var fed = timeframe == Timeframe.M1 ? Sessions : Closed(timeframe);
            foreach (var spec in TestSpecs.Variants(timeframe))
            {
                IndicatorCatalog.TryGet(spec.Name, out var info).ShouldBeTrue();
                var indicator = info.Create(spec);
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
