#pragma warning disable CA2012 // NSubstitute setup and verification calls on ValueTask members are not awaited
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.LearningPhase;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Tests.LearningPhase;

/// <summary>Spec P15-12 over fake read ports: the facts the handler builds (versions, effective N, the switch, coverage, seasons, the share), a missing or unreadable source is NOT EVALUABLE or unavailable and never an error or a MET, and an unknown cut is refused.</summary>
public sealed class GetEventsPhaseCheckHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 6, 30, 0, TimeSpan.Zero);

    private readonly IExtensionEvaluationReader _evaluations = Substitute.For<IExtensionEvaluationReader>();

    private readonly IEventFilterHistory _history = Substitute.For<IEventFilterHistory>();

    private readonly IFilingReader _filings = Substitute.For<IFilingReader>();

    private readonly IFilingInstruments _instruments = Substitute.For<IFilingInstruments>();

    private readonly IResultsCalendar _calendar = Substitute.For<IResultsCalendar>();

    private readonly IFilingSettings _settings = Substitute.For<IFilingSettings>();

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();

    private readonly ISimulationEngineVersion _engine = Substitute.For<ISimulationEngineVersion>();

    private readonly Dictionary<string, List<ExtensionEvaluationRecord>> _records = [];

    public GetEventsPhaseCheckHandlerTests()
    {
        _settings.LagMinutes.Returns(0);
        _engine.EngineVersion.Returns("sim-9");
        _evaluations.ListAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call => ValueTask.FromResult<IReadOnlyList<ExtensionEvaluationRecord>>(_records.TryGetValue(call.Arg<string>(), out var list) ? list : []));
        _history.RowsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EventFilterRow>>([]));
        _filings.CoverageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new FilingCoverageData([new FilingCoverageCell("results", new DateOnly(2026, 9, 30), 10, 0, 0, new Dictionary<string, int>())], 20, new Dictionary<string, int> { ["results"] = 2 })));
        _instruments.TrackedAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<IReadOnlyList<TrackedInstrument>>([]));
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new BarTotals(10, 1000, new DateTimeOffset(2025, 1, 6, 4, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 30, 4, 0, 0, TimeSpan.Zero), 0, 0)));
    }

    private GetEventsPhaseCheckHandler Handler() => new(_evaluations, _history, _filings, _instruments, _calendar, _settings, _bars, _engine, new FakeTimeProvider(Now), NullLogger<GetEventsPhaseCheckHandler>.Instance);

    private static ExtensionEvaluationRecord Record(string id, string family, string protocol, string verdict, string versionKey, string? extensionVersion = null, string sources = "", string estimates = "{}", string[]? missing = null, bool holdOut = false) =>
        new(id, family, protocol, extensionVersion, versionKey, "tf-1", new string('b', 64), null, null, 3, 1.0, "quarter", 4, 4, 300, null, estimates, verdict, missing ?? [], [], "{}", "{\"sources\":{" + sources + "}}", holdOut);

    private void Add(ExtensionEvaluationRecord record)
    {
        if (!_records.TryGetValue(record.Family, out var list))
        {
            _records[record.Family] = list = [];
        }

        list.Add(record);
    }

    private void StoreAllCurrent(string filterVerdict = "KEEP")
    {
        Add(Record("f2", ExtensionFamilies.Filter, "F", filterVerdict, "evf-1:N=2", sources: "\"rule_version\":\"evf-1\",\"lag_minutes\":\"0\"", estimates: "{\"share_lost_to_no_upcoming_entry\":0.2}"));
        Add(Record("ev", ExtensionFamilies.EventFeatures, "X", "KEEP", "ev-1:X", "ev-1", "\"lag_minutes\":\"0\""));
        Add(Record("fu", ExtensionFamilies.FundamentalFeatures, "X", "DROP", "fu-1:X", "fu-1", "\"lag_minutes\":\"0\",\"parser_version\":\"" + ResultsParser.Version + "\""));
        Add(Record("l", ExtensionFamilies.Flag, "L", "KEEP", "llm-1:L", "llm-1"));
        Add(Record("lx", ExtensionFamilies.Flag, "X", "KEEP", "llm-1:X", "llm-1"));
        Add(Record("p", ExtensionFamilies.Pead, "P", "KEEP", "pead-1", sources: "\"engine\":\"sim-9\""));
    }

    private void FilterOn(int id, string decidedBy, int days = 2) =>
        _history.RowsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EventFilterRow>>([new EventFilterRow(id, true, days, "manual", decidedBy, decidedBy == "owner" ? "owner:x" : "auto:event-filter", "r", "{}", null, Now)]));

    [Fact]
    public async Task Nothing_stored_is_not_evaluable_with_the_coverage_and_seasons_read()
    {
        var result = await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken);

        var view = result.Value;
        view.Report.Verdict.ShouldBe("NOT EVALUABLE");
        view.Report.Summary.ShouldBe("no P15 evaluation stored.");
        view.Report.SeasonsHeld.ShouldBe(8);
        view.Report.Filings.ShouldNotBeNull().Single(k => k.Kind == "results").Stored.ShouldBe(10);
        view.Report.TrackedInstruments.ShouldBe(0);
        view.Text.ShouldContain("limits:");
    }

    [Fact]
    public async Task Every_family_resolved_for_the_current_versions_with_the_filter_off_is_met_and_the_share_comes_from_the_newest_f_record()
    {
        StoreAllCurrent();

        var view = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;

        view.Report.Verdict.ShouldBe("MET");
        view.Report.NoUpcomingShare.ShouldBe(0.2);
        view.Report.NoUpcomingRecord.ShouldBe("f2");
    }

    [Fact]
    public async Task Stored_expression_pairs_and_iv_records_never_reach_the_phase_check_which_stays_byte_identical_and_never_asks_for_their_families()
    {
        StoreAllCurrent();
        var before = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;
        Add(Record("x1", ExpressionFamilies.OptionBuy, "O", "KEEP", "expr-1"));
        Add(Record("s1", PairFamilies.Intraday, "S", "KEEP", "pairs-1"));
        Add(Record("i1", IvFeatureCatalog.Family, "X", "KEEP", "iv-1"));

        var after = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;

        after.Text.ShouldBe(before.Text);
        after.Json.ShouldBe(before.Json);
        await _evaluations.DidNotReceive().ListAsync(ExpressionFamilies.OptionBuy, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _evaluations.DidNotReceive().ListAsync(PairFamilies.Intraday, Arg.Any<int>(), Arg.Any<CancellationToken>());
        await _evaluations.DidNotReceive().ListAsync(IvFeatureCatalog.Family, Arg.Any<int>(), Arg.Any<CancellationToken>());
        ExtensionFamilies.All.ShouldNotContain(IvFeatureCatalog.Family);
        ExtensionFamilies.All.ShouldNotContain(ExpressionFamilies.OptionBuy);
    }

    [Fact]
    public async Task The_filter_on_with_a_drop_for_its_window_is_not_met_and_names_the_row_and_who_decided()
    {
        StoreAllCurrent("DROP");
        FilterOn(12, "auto");

        var view = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;

        view.Report.Verdict.ShouldBe("NOT MET");
        var violation = view.Report.Violations.ShouldHaveSingleItem();
        (violation.DecisionId, violation.DecidedBy).ShouldBe((12L, "auto"));
    }

    [Fact]
    public async Task A_verdict_for_another_window_than_the_switch_is_not_the_verdict_of_the_filter()
    {
        StoreAllCurrent();
        FilterOn(5, "owner", days: 3);

        var view = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;

        view.Report.Violations.ShouldHaveSingleItem();
        view.Report.Families.Single(f => f.Family == ExtensionFamilies.Filter).Status.ShouldBe("none");
    }

    [Theory]
    [InlineData("\"rule_version\":\"evf-0\",\"lag_minutes\":\"0\"")]
    [InlineData("\"rule_version\":\"evf-1\",\"lag_minutes\":\"15\"")]
    [InlineData("\"rule_version\":\"evf-1\"")]
    public async Task A_record_of_another_rule_or_lag_or_with_no_version_recorded_is_stale(string sources)
    {
        StoreAllCurrent();
        _records[ExtensionFamilies.Filter] = [Record("old", ExtensionFamilies.Filter, "F", "KEEP", "evf-1:N=2", sources: sources)];

        var view = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;

        view.Report.Verdict.ShouldBe("NOT EVALUABLE");
        view.Report.Families.Single(f => f.Family == ExtensionFamilies.Filter).Status.ShouldBe("stale");
    }

    [Fact]
    public async Task A_pead_record_of_another_engine_and_a_fundamental_record_of_another_parser_are_stale()
    {
        StoreAllCurrent();
        _records[ExtensionFamilies.Pead] = [Record("p", ExtensionFamilies.Pead, "P", "KEEP", "pead-1", sources: "\"engine\":\"sim-8\"")];
        _records[ExtensionFamilies.FundamentalFeatures] = [Record("fu", ExtensionFamilies.FundamentalFeatures, "X", "KEEP", "fu-1:X", "fu-1", "\"lag_minutes\":\"0\",\"parser_version\":\"xbrl-0\"")];

        var view = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;

        view.Report.Families.Single(f => f.Family == ExtensionFamilies.Pead).Status.ShouldBe("stale");
        view.Report.Families.Single(f => f.Family == ExtensionFamilies.FundamentalFeatures).Status.ShouldBe("stale");
        view.Report.Verdict.ShouldBe("NOT EVALUABLE");
    }

    [Fact]
    public async Task A_store_that_cannot_be_read_makes_the_family_not_evaluable_and_is_not_an_error()
    {
        StoreAllCurrent();
        _evaluations.ListAsync(ExtensionFamilies.Pead, Arg.Any<int>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("relation does not exist"));

        var result = await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Report.Verdict.ShouldBe("NOT EVALUABLE");
        result.Value.Report.Families.Single(f => f.Family == ExtensionFamilies.Pead).Status.ShouldBe("unreadable");
    }

    [Fact]
    public async Task An_unreadable_decision_history_reads_as_filter_off_and_the_other_sources_as_unavailable()
    {
        StoreAllCurrent();
        _history.RowsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("no table"));
        _filings.CoverageAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("no table"));
        _bars.GetBarTotalsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("down"));
        _instruments.TrackedAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("down"));

        var report = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value.Report;

        (report.FilterOn, report.Filings, report.SeasonsHeld, report.TrackedInstruments).ShouldBe((false, null, null, null));
        report.Verdict.ShouldBe("NOT EVALUABLE");
        report.Open.ShouldContain(o => o.Contains("decision history could not be read"));
    }

    [Fact]
    public async Task The_flag_family_with_a_passed_grade_and_a_comparison_that_is_not_evaluable_stays_open()
    {
        StoreAllCurrent();
        _records[ExtensionFamilies.Flag] = [Record("lx", ExtensionFamilies.Flag, "X", "NOT_EVALUABLE", "llm-1:X", "llm-1", missing: ["no_extraction: the llm-1 feature rows are not built (P15-10)"]), Record("l", ExtensionFamilies.Flag, "L", "KEEP", "llm-1:L", "llm-1")];

        var view = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;

        view.Report.Verdict.ShouldBe("NOT EVALUABLE");
        view.Text.ShouldContain("no_extraction");
    }

    [Fact]
    public async Task A_cut_is_passed_through_and_an_unknown_family_is_refused_without_reading_anything()
    {
        var cut = await Handler().HandleAsync(new EventsPhaseCheckQuery([ExtensionFamilies.Pead]), TestContext.Current.CancellationToken);
        var refused = await Handler().HandleAsync(new EventsPhaseCheckQuery(["ext:nope"]), TestContext.Current.CancellationToken);

        cut.Value.Report.Cut.ShouldBe([ExtensionFamilies.Pead]);
        refused.IsFailure.ShouldBeTrue();
        refused.Error.Code.ShouldBe("events.unknown_family");
    }

    [Fact]
    public async Task The_same_stored_rows_give_the_same_output()
    {
        StoreAllCurrent();

        var a = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;
        var b = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value;

        (a.Text, a.Json).ShouldBe((b.Text, b.Json));
    }

    [Fact]
    public async Task A_pead_record_with_no_events_for_the_current_engine_reads_not_evaluable_not_stale()
    {
        StoreAllCurrent();
        _records[ExtensionFamilies.Pead] = [Record("p", ExtensionFamilies.Pead, "P", "NOT_EVALUABLE", "pead-1", sources: "\"engine\":\"sim-9\",\"source\":\"no PEAD events\"", missing: ["seasons 0 of 20"])];

        var line = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value.Report.Families.Single(f => f.Family == ExtensionFamilies.Pead);

        line.Status.ShouldBe("NOT_EVALUABLE");
        line.Detail.ShouldContain("seasons 0 of 20");
    }

    [Fact]
    public async Task The_filter_on_with_an_unreadable_verdict_store_is_not_evaluable_not_not_met()
    {
        StoreAllCurrent();
        FilterOn(3, "owner");
        _evaluations.ListAsync(ExtensionFamilies.Filter, Arg.Any<int>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("no table"));

        var report = (await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken)).Value.Report;

        report.Verdict.ShouldBe("NOT EVALUABLE");
        report.Violations.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_handler_only_calls_read_members()
    {
        StoreAllCurrent();

        await Handler().HandleAsync(new EventsPhaseCheckQuery([]), TestContext.Current.CancellationToken);

        _evaluations.ReceivedCalls().ShouldAllBe(c => c.GetMethodInfo().Name == "ListAsync");
        _history.ReceivedCalls().ShouldAllBe(c => c.GetMethodInfo().Name == "RowsAsync");
    }
}
