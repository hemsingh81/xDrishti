using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>
/// P15-06 criterion 7 (ADR 0182, rule version lt-3): the <c>news_results</c> tag. A loss is tagged when an event instant of its instrument lies in [entry, exit], both inclusive; the broadcast of a results filing first, the 09:15 session open of a calendar meeting when there is no filing,
/// saying so; assigned before NormalVariance; events not supplied means no tag and a count, never "no news". The tag reads only the closed trade and the instants given.
/// </summary>
public sealed class LossTagNewsResultsTests
{
    private static readonly LossTagOptions Options = new();

    /// <summary>A plain stop-out of one risk on Monday 2026-03-02 held an hour from 10:00 IST (04:30 UTC).</summary>
    private static SimTrade Trade()
    {
        var t = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:A", Side.Buy, FabricatedRun.Monday, ExitReason.Stop, entryMinute: 600, entry: 100, stop: 99, target: 102, exitPrice: 99, rExact: -1, rFilled: -1);
        return t with { MaeR = 1, MfeR = 0.2, ExitEpochSeconds = t.EntryEpochSeconds + 3600 };
    }

    private static DateTimeOffset Entry(SimTrade t) => DateTimeOffset.FromUnixTimeSeconds(t.EntryEpochSeconds);

    private static DateTimeOffset Exit(SimTrade t) => DateTimeOffset.FromUnixTimeSeconds(t.ExitEpochSeconds);

    private static LossTagKind[] Kinds(SimTrade t, params EventInstant[] events) => [.. LossTagger.Tag(t, Options, events).Select(x => x.Kind)];

    [Fact]
    public void The_rule_version_is_lt_3_and_the_new_kind_is_appended_last_so_stored_counts_keep_their_order()
    {
        LossTagger.RuleVersion.ShouldBe("lt-3");
        Enum.GetNames<LossTagKind>().ShouldBe(["GapThroughStop", "GaveBackProfit", "HighSlippage", "LateEntry", "QuickStop", "WideStop", "NormalVariance", "NewsResults"]);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(1800, true)]
    [InlineData(3599, true)]
    [InlineData(3600, true)]
    [InlineData(3601, false)]
    public void An_event_instant_is_in_the_span_from_the_entry_to_the_exit_both_ends_included(int secondsAfterEntry, bool tagged)
    {
        var t = Trade();

        Kinds(t, new EventInstant(Entry(t).AddSeconds(secondsAfterEntry), false)).Contains(LossTagKind.NewsResults).ShouldBe(tagged);
    }

    [Fact]
    public void A_results_loss_is_not_called_ordinary_variance()
    {
        var t = Trade();

        Kinds(t).ShouldBe([LossTagKind.NormalVariance]);
        Kinds(t, new EventInstant(Entry(t).AddMinutes(10), false)).ShouldBe([LossTagKind.NewsResults]);
    }

    [Fact]
    public void The_detail_names_the_broadcast_and_the_span_in_india_time_and_the_value_is_the_minutes_from_the_entry()
    {
        var t = Trade();
        var broadcast = new EventInstant(Entry(t).AddMinutes(25), false);

        var tag = LossTagger.Tag(t, Options, [broadcast]).Single(x => x.Kind == LossTagKind.NewsResults);

        tag.Value.ShouldBe(25, 1e-9);
        tag.Detail.ShouldBe("results broadcast 2026-03-02 10:25 IST while the trade was open (entry 2026-03-02 10:00 IST, exit 2026-03-02 11:00 IST)");
    }

    [Fact]
    public void A_calendar_basis_says_so_and_a_broadcast_wins_the_tie_with_a_session_open_at_the_same_instant()
    {
        var t = Trade();
        var at = Entry(t).AddMinutes(5);

        var calendar = LossTagger.Tag(t, Options, [new EventInstant(at, true)]).Single(x => x.Kind == LossTagKind.NewsResults);
        var both = LossTagger.Tag(t, Options, [new EventInstant(at, true), new EventInstant(at, false)]).Single(x => x.Kind == LossTagKind.NewsResults);

        calendar.Detail.ShouldEndWith("; basis: calendar");
        calendar.Detail.ShouldStartWith("results meeting session 2026-03-02 10:05 IST");
        both.Detail.ShouldNotContain("basis: calendar");
    }

    [Fact]
    public void Events_not_supplied_give_no_tag_while_an_empty_list_means_supplied_and_none()
    {
        var t = Trade();

        LossTagger.Tag(t, Options).Select(x => x.Kind).ShouldBe([LossTagKind.NormalVariance]);
        LossTagger.Tag(t, Options, null).Select(x => x.Kind).ShouldBe([LossTagKind.NormalVariance]);
        LossTagger.Tag(t, Options, []).Select(x => x.Kind).ShouldBe([LossTagKind.NormalVariance]);
    }

    [Fact]
    public void The_tag_reads_the_trade_and_the_events_only_so_the_same_inputs_give_the_same_tags_in_any_event_order()
    {
        var t = Trade();
        var events = new[] { new EventInstant(Entry(t).AddMinutes(40), false), new EventInstant(Entry(t).AddMinutes(10), true) };

        var a = LossTagger.Tag(t, Options, events);
        var b = LossTagger.Tag(t, Options, [.. events.Reverse()]);

        b.ShouldBe(a);
        a.Single(x => x.Kind == LossTagKind.NewsResults).Value.ShouldBe(10, 1e-9);
    }

    [Fact]
    public void The_analyzer_counts_the_losses_of_instruments_without_events_as_unavailable_and_never_as_no_news()
    {
        var withEvents = Loss(FabricatedRun.IdA);
        var without = Loss(FabricatedRun.IdB, 1);
        var events = new Dictionary<Guid, IReadOnlyList<EventInstant>> { [FabricatedRun.IdA] = [new EventInstant(Entry(withEvents.Trade).AddMinutes(5), false)] };

        var report = LossTagAnalyzer.Analyze([withEvents, without], Options, events).Value;

        report.Cells.Single().Counts[(int)LossTagKind.NewsResults].ShouldBe(1);
        report.NewsResultsUnavailable.ShouldBe(1);
        report.NewsResultsInstrumentsWithoutData.ShouldBe(1);
        report.EventsSupplied.ShouldBeTrue();
        report.Limits.ShouldContain(l => l.Contains("1 losses of 1 instruments"));
    }

    [Fact]
    public void Without_events_every_loss_is_unavailable_the_not_built_line_stays_and_the_comparisons_are_unchanged()
    {
        var losses = Enumerable.Range(0, 4).Select(i => Loss(FabricatedRun.IdA, i)).ToList();

        var without = LossTagAnalyzer.Analyze(losses, Options).Value;
        var supplied = LossTagAnalyzer.Analyze(losses, Options, new Dictionary<Guid, IReadOnlyList<EventInstant>> { [FabricatedRun.IdA] = [] }).Value;

        without.NewsResultsUnavailable.ShouldBe(4);
        without.EventsSupplied.ShouldBeFalse();
        LossTagReport.NotBuiltFor(without.EventsSupplied, without.NewsResultsInstrumentsWithoutData).ShouldContain(l => l == "news_results: events not supplied");
        LossTagReport.NotBuiltFor(supplied.EventsSupplied, supplied.NewsResultsInstrumentsWithoutData).ShouldNotContain(l => l.StartsWith("news_results", StringComparison.Ordinal), "the line leaves only when the events were supplied");
        supplied.NewsResultsUnavailable.ShouldBe(0);
        without.Comparisons.ShouldBe(0);
        without.ToText().ShouldContain("not built: news_results: events not supplied");
    }

    [Fact]
    public void The_growing_tag_detector_compares_the_new_kind_only_when_events_were_supplied()
    {
        var losses = Enumerable.Range(0, 12).Select(i => Loss(FabricatedRun.IdA, i)).ToList();
        var options = new LossTagOptions(GrowingWindow: 5);

        var without = LossTagAnalyzer.Analyze(losses, options).Value;
        var supplied = LossTagAnalyzer.Analyze(losses, options, new Dictionary<Guid, IReadOnlyList<EventInstant>> { [FabricatedRun.IdA] = [] }).Value;

        supplied.Comparisons.ShouldBe(without.Comparisons + 1);
    }

    [Fact]
    public void The_type_is_a_description_of_the_past_and_stored_nowhere_so_it_has_no_member_a_feature_could_read()
    {
        typeof(EventInstant).GetProperties().Select(p => p.Name).ShouldBe(["At", "FromCalendar"]);
        typeof(LossTagger).GetMethods().Where(m => m.Name == "Tag").ShouldAllBe(m => m.GetParameters()[0].ParameterType == typeof(SimTrade));
    }

    private static LossTagInput Loss(Guid instrument, int day = 0)
    {
        var t = FabricatedRun.Trade(instrument, "NSE:A", Side.Buy, FabricatedRun.Monday.AddDays(day), ExitReason.Stop, entryMinute: 600, entry: 100, stop: 99, target: 102, exitPrice: 99, rExact: -1, rFilled: -1);
        t = t with { MaeR = 1, MfeR = 0.2, ExitEpochSeconds = t.EntryEpochSeconds + 3600 };
        return new LossTagInput("cell", t, -1);
    }
}
