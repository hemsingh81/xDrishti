using System.Reflection;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests;

/// <summary>
/// <see cref="IndicatorInfo.RestartsEachSession"/> (ADR 0035): which features restart at each India date <i>in the frame</i>, so that their value history is the session's only and a coarser lane is pending until it
/// closes its first bar of the date. The twelve names are written out here, independently of the catalog, and the answer is pinned for all 56 names on all nine timeframes. This replaces the test of
/// <c>HasValueAtSessionOpen</c>, which asked a different question (does the feature have a value on the first bar) and no longer exists: the frame's rule is keyed on the kind of feature, not on its first value.
/// </summary>
public sealed class IndicatorInfoRestartTests
{
    /// <summary>The twelve features that start again at each session (written out; not derived from the catalog).</summary>
    private static readonly string[] Restarting =
    [
        "vwap", "or_high", "or_low", "today_open", "today_high", "today_low", "prev_day_open", "prev_day_high", "prev_day_low", "prev_day_close", "prev_day_volume", "gap_pct",
    ];

    private static readonly Timeframe[] Intraday = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1];

    private static readonly Timeframe[] Coarse = [Timeframe.D1, Timeframe.W1, Timeframe.Mo1];

    public static TheoryData<string, Timeframe> EveryNameOnEveryTimeframe
    {
        get
        {
            var data = new TheoryData<string, Timeframe>();
            foreach (var info in IndicatorCatalog.All)
            {
                foreach (var timeframe in Enum.GetValues<Timeframe>())
                {
                    data.Add(info.Name, timeframe);
                }
            }

            return data;
        }
    }

    /// <summary>A spec of the feature on the timeframe whose parameters are valid there (an opening range of one bar of the timeframe).</summary>
    private static FeatureSpec SpecOn(IndicatorInfo info, Timeframe timeframe)
    {
        var spec = info.DefaultSpec(timeframe);
        return info.Name.StartsWith("or_", StringComparison.Ordinal) ? spec with { P1 = Math.Max(timeframe.Minutes(), 1) } : spec;
    }

    [Theory]
    [MemberData(nameof(EveryNameOnEveryTimeframe))]
    public void Only_the_twelve_names_on_the_six_intraday_timeframes_restart_each_session(string name, Timeframe timeframe)
    {
        IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();
        var expected = Restarting.Contains(name) && Intraday.Contains(timeframe);

        info.RestartsEachSession(info.DefaultSpec(timeframe)).ShouldBe(expected, name + " on " + timeframe.ToCode());
    }

    [Fact]
    public void The_twelve_names_are_written_out_once_and_are_exactly_the_per_session_ones_of_the_catalog()
    {
        IndicatorCatalog.All.Count.ShouldBe(56);
        Restarting.Distinct().Count().ShouldBe(12);
        IndicatorCatalog.All.Where(i => i.SessionBehaviour == SessionBehaviour.PerSession).Select(i => i.Name).ShouldBe(Restarting, ignoreOrder: true);
        IndicatorCatalog.All.Where(i => Intraday.Any(t => i.RestartsEachSession(i.DefaultSpec(t)))).Select(i => i.Name).ShouldBe(Restarting, ignoreOrder: true);
    }

    [Fact]
    public void Where_the_catalog_refuses_a_feature_it_does_not_restart_either()
    {
        foreach (var info in IndicatorCatalog.All)
        {
            foreach (var timeframe in Enum.GetValues<Timeframe>().Where(t => !info.Supports(t)))
            {
                info.RestartsEachSession(info.DefaultSpec(timeframe)).ShouldBeFalse(info.Name + " on " + timeframe.ToCode());
            }
        }

        foreach (var timeframe in Coarse)
        {
            IndicatorCatalog.All.Where(i => i.RestartsEachSession(i.DefaultSpec(timeframe))).ShouldBeEmpty("nothing restarts on " + timeframe.ToCode() + ": a daily bar is a whole session");
        }
    }

    [Fact]
    public void Every_restarting_feature_is_created_as_an_indicator_the_frame_can_mark_pending()
    {
        foreach (var info in IndicatorCatalog.All)
        {
            foreach (var timeframe in Intraday)
            {
                var spec = SpecOn(info, timeframe);
                var created = info.Create(spec);

                (created is IRestartingIndicator).ShouldBe(info.RestartsEachSession(spec), info.Name + " on " + timeframe.ToCode());
            }
        }
    }

    [Fact]
    public void The_question_of_the_first_value_is_gone_the_frame_rule_does_not_depend_on_it()
    {
        typeof(IndicatorInfo).GetMethod("HasValueAtSessionOpen", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).ShouldBeNull();
    }

    [Theory]
    [InlineData("or_high", Timeframe.M1, 1)]
    [InlineData("or_high", Timeframe.M1, 15)]
    [InlineData("or_low", Timeframe.M5, 5)]
    [InlineData("or_low", Timeframe.M15, 30)]
    [InlineData("or_high", Timeframe.H1, 120)]
    public void An_opening_range_restarts_whether_it_is_one_bar_of_the_timeframe_or_several(string name, Timeframe timeframe, int minutes)
    {
        IndicatorCatalog.TryGet(name, out var info).ShouldBeTrue();

        info.RestartsEachSession(info.DefaultSpec(timeframe) with { P1 = minutes }).ShouldBeTrue();
    }
}
