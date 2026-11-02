#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Learning;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.Learning;

/// <summary>
/// P15-05 criteria 3, 4, 5 and 9 through the use case: the rows and the one record (lag, parser version, calendar hash, newest broadcast time, NaN counts) of an extraction, the refusals before any read, the second call that stores nothing and says so, and the same rows for the same run
/// whatever order the trades come in.
/// </summary>
public sealed class ExtractExtensionFeaturesHandlerTests
{
    private static readonly string Key = new('a', 64);

    private static readonly DateOnly Day = new(2026, 7, 29);

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRunStore _runs = Substitute.For<IRunStore>();

    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();

    private readonly IEventCalendarSnapshots _snapshots = Substitute.For<IEventCalendarSnapshots>();

    private readonly IResultsFilingTimes _filingTimes = Substitute.For<IResultsFilingTimes>();

    private readonly IContextDayStore _context = Substitute.For<IContextDayStore>();

    private readonly IFilingSettings _settings = Substitute.For<IFilingSettings>();

    private readonly IBarQueries _bars = Substitute.For<IBarQueries>();

    private readonly IAdjustmentFactors _factors = Substitute.For<IAdjustmentFactors>();

    private readonly IResultFactsAsOf _facts = Substitute.For<IResultFactsAsOf>();

    private readonly IShareholdingAsOf _holdings = Substitute.For<IShareholdingAsOf>();

    private readonly IExtensionFeatureStore _rows = Substitute.For<IExtensionFeatureStore>();

    private readonly IExtensionExtractionStore _records = Substitute.For<IExtensionExtractionStore>();

    private IReadOnlyList<ExtensionFeatureRow>? _written;

    private ExtensionExtractionRecord? _record;

    private static readonly DateTimeOffset Filed = new(2026, 7, 24, 16, 12, 0, Ist);

    private ExtractExtensionFeaturesHandler Handler()
    {
        _settings.LagMinutes.Returns(5);
        _calendar.GetAsync(Ct).Returns(new TradingCalendar());
        _snapshots.ReadAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult(Result.Success(EventCalendarSnapshot.Create([], 5))));
        _filingTimes.ReadAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<ResultsFilingTime>>([new ResultsFilingTime(FabricatedRun.IdA, new DateOnly(2026, 6, 30), Filed.ToUniversalTime())]));
        _context.ListAsync(Ct).Returns(ValueTask.FromResult<IReadOnlyList<ContextDayRecord>>([]));
        _bars.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<BarRow>>([]));
        _factors.GetAsync(Arg.Any<Guid>(), Ct).Returns(XDrishti.Domain.Market.Actions.AdjustmentFactors.None);
        _facts.Facts(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Ct).Returns(ValueTask.FromResult(new ResultFactsAnswer([], 0) { ParserVersion = ResultsParser.Version }));
        _holdings.Latest(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Ct).Returns(ValueTask.FromResult(new ShareholdingAnswer(ShareholdingAnswer.Unavailable, null, null, ShareholdingAnswer.NoEntry)));
        _rows.AppendWithRecordAsync(Arg.Do<IReadOnlyList<ExtensionFeatureRow>>(r => _written = r), Arg.Do<ExtensionExtractionRecord>(r => _record = r), Arg.Any<DateTimeOffset>(), Ct)
            .Returns(ci => ValueTask.FromResult((ci.ArgAt<IReadOnlyList<ExtensionFeatureRow>>(0).Count, true)));
        var extractor = new ExtensionFeatureExtractor(_bars, _factors, _facts, _holdings);
        return new ExtractExtensionFeaturesHandler(_runs, _calendar, _snapshots, _filingTimes, _context, _settings, extractor, _rows, _records, new FakeTimeProvider(new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero)), NullLogger<ExtractExtensionFeaturesHandler>.Instance);
    }

    private static StoredRunHeader Header() =>
        new(1, Key, "sim", 5, 5, "ORB", new string('b', 64), new DateOnly(2026, 1, 1), new DateOnly(2026, 11, 1), "basket", "x", 1, 1, 0, 0, 0, 0, false, "{}", null, null, 0, new string('c', 64), new string('d', 64), DateTimeOffset.UnixEpoch, "t");

    private static async IAsyncEnumerable<RunTrade> Stream(SimTrade[] trades)
    {
        foreach (var t in trades)
        {
            await Task.Yield();
            yield return new RunTrade(t, []);
        }
    }

    private void Run(params SimTrade[] trades)
    {
        _runs.FindAsync(Key, Ct).Returns(Header());
        _runs.StreamTradesAsync(Key, false, Ct).Returns(Stream(trades));
    }

    private static SimTrade Trade(Guid instrument, int dayOffset = 0) => FabricatedRun.Trade(instrument, "NSE:A", Side.Buy, Day.AddDays(dayOffset));

    [Fact]
    public async Task The_event_features_are_stored_for_every_trade_at_its_decision_instant_with_one_record_of_what_they_were_built_from()
    {
        var trade = Trade(FabricatedRun.IdA);
        Run(trade);

        var result = await Handler().HandleAsync(new ExtractExtensionFeaturesCommand(Key, "ev-1"), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new ExtensionFeaturesOutcome("ev-1", 1, 1, 2, false));
        var row = _written.ShouldNotBeNull().Single();
        (row.RunKey, row.TradeKey, row.Extension, row.Version).ShouldBe((Key, trade.Key, "ev", "ev-1"));
        row.DecisionAt.ToUnixTimeSeconds().ShouldBe(trade.DecisionEpochSeconds);
        row.Values.Count.ShouldBe(3);
        row.Values[1].ShouldBe(2.0, "the filing of Friday 2026-07-24 reacts on Monday 2026-07-27; the decision date is Wednesday: two sessions later");
        double.IsNaN(row.Values[0]).ShouldBeTrue("no calendar row: unknown, never zero");
        double.IsNaN(row.Values[2]).ShouldBeTrue("no bars and no market context: unknown");
        var record = _record.ShouldNotBeNull();
        (record.RunKey, record.Extension, record.Version, record.LagMinutes, record.ParserVersion, record.Rows).ShouldBe((Key, "ev", "ev-1", 5, null, 1));
        record.CalendarSha256.ShouldBe(EventCalendarSnapshot.Create([], 5).Sha256);
        record.FilingsWatermark.ShouldBe(Filed.ToUniversalTime());
        record.NanCountsJson.ShouldBe("{\"nan\":{\"days_to_results\":{\"no_calendar\":1},\"days_since_results\":{},\"results_car3\":{\"no_bars\":1}},\"parser_unverified\":{\"days_to_results\":0,\"days_since_results\":0,\"results_car3\":0}}");
    }

    [Fact]
    public async Task The_fundamental_features_record_the_parser_version_and_count_unknowns_per_reason()
    {
        Run(Trade(FabricatedRun.IdA));

        var result = await Handler().HandleAsync(new ExtractExtensionFeaturesCommand(Key, "fu-1"), Ct);

        result.Value.Version.ShouldBe("fu-1");
        _written.ShouldNotBeNull().Single().Values.Count.ShouldBe(7);
        _written.Single().Extension.ShouldBe("fu");
        _record.ShouldNotBeNull().ParserVersion.ShouldBe("xbrl-1-synthetic");
        _record.NanCountsJson.ShouldContain("\"sue_yoy\":{\"no_filing\":1}");
        result.Value.UnknownValues.ShouldBe(7, "no facts and no shareholding: every value is unknown with its reason");
    }

    [Fact]
    public async Task A_second_call_finds_the_record_stores_nothing_and_says_so()
    {
        Run(Trade(FabricatedRun.IdA));
        var handler = Handler();
        _records.FindAsync(Key, "ev-1", Ct).Returns(ValueTask.FromResult<ExtensionExtractionRecord?>(new ExtensionExtractionRecord(Key, "ev", "ev-1", 5, null, new string('0', 64), null, 1, "{}", DateTimeOffset.UnixEpoch)));

        var result = await handler.HandleAsync(new ExtractExtensionFeaturesCommand(Key, "ev-1"), Ct);

        result.Value.ShouldBe(new ExtensionFeaturesOutcome("ev-1", 1, 0, 0, true));
        await _rows.DidNotReceiveWithAnyArgs().AppendWithRecordAsync(default!, default!, default, Ct);
        await _snapshots.DidNotReceiveWithAnyArgs().ReadAsync(default!, default, Ct);
    }

    [Fact]
    public async Task The_rows_and_the_record_are_written_in_one_call_so_the_record_always_describes_the_stored_rows()
    {
        Run(Trade(FabricatedRun.IdA), Trade(FabricatedRun.IdA, 1));

        await Handler().HandleAsync(new ExtractExtensionFeaturesCommand(Key, "ev-1"), Ct);

        await _rows.Received(1).AppendWithRecordAsync(Arg.Any<IReadOnlyList<ExtensionFeatureRow>>(), Arg.Any<ExtensionExtractionRecord>(), Arg.Any<DateTimeOffset>(), Ct);
        await _rows.DidNotReceiveWithAnyArgs().AppendAsync(default!, default, Ct);
        _record.ShouldNotBeNull().Rows.ShouldBe(_written.ShouldNotBeNull().Count);
    }

    [Fact]
    public async Task The_daily_bars_are_read_once_per_instrument_up_to_the_day_after_the_last_decision_and_never_the_minute_bars()
    {
        var a = Trade(FabricatedRun.IdA);
        var a2 = Trade(FabricatedRun.IdA, 3);
        Run(a, a2);

        await Handler().HandleAsync(new ExtractExtensionFeaturesCommand(Key, "fu-1"), Ct);

        await _bars.Received(1).ReadDailyBarsAsync(FabricatedRun.IdA, new DateOnly(2000, 1, 1), Day.AddDays(3).AddDays(1), Ct);
        _bars.ReceivedCalls().Select(c => c.GetMethodInfo().Name).Distinct().ShouldBe(["ReadDailyBarsAsync"]);
    }

    [Theory]
    [InlineData("not-a-key", "ev-1")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "tf-1")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "")]
    public async Task A_bad_run_key_or_extension_is_refused_before_anything_is_read(string runKey, string version)
    {
        var result = await Handler().HandleAsync(new ExtractExtensionFeaturesCommand(runKey, version), Ct);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(LearningErrors.InvalidCode);
        await _runs.DidNotReceiveWithAnyArgs().FindAsync(default!, Ct);
        await _rows.DidNotReceiveWithAnyArgs().AppendWithRecordAsync(default!, default!, default, Ct);
    }

    [Fact]
    public async Task An_unknown_run_is_not_found()
    {
        _runs.FindAsync(Key, Ct).Returns((StoredRunHeader?)null);

        var result = await Handler().HandleAsync(new ExtractExtensionFeaturesCommand(Key, "ev-1"), Ct);

        result.Error.Code.ShouldBe(LearningErrors.RunNotFoundCode);
    }

    [Fact]
    public async Task A_calendar_that_cannot_be_read_refuses_the_extraction_and_stores_nothing()
    {
        Run(Trade(FabricatedRun.IdA));
        var handler = Handler();
        _snapshots.ReadAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult(Result.Failure<EventCalendarSnapshot>(Error.Validation("simulation.filter_not_available", "unreadable"))));

        var result = await handler.HandleAsync(new ExtractExtensionFeaturesCommand(Key, "ev-1"), Ct);

        result.IsFailure.ShouldBeTrue();
        await _rows.DidNotReceiveWithAnyArgs().AppendWithRecordAsync(default!, default!, default, Ct);
    }

    [Fact]
    public async Task The_same_run_gives_the_same_rows_and_record_whatever_order_the_trades_come_in()
    {
        var a = Trade(FabricatedRun.IdA);
        var a2 = Trade(FabricatedRun.IdA, 1);
        var b = Trade(FabricatedRun.IdB);

        Run(a, b, a2);
        await Handler().HandleAsync(new ExtractExtensionFeaturesCommand(Key, "ev-1"), Ct);
        var first = string.Join(";", _written.ShouldNotBeNull().Select(Show)) + "#" + _record.ShouldNotBeNull().NanCountsJson;

        _written = null;
        _record = null;
        Run(a2, b, a);
        await Handler().HandleAsync(new ExtractExtensionFeaturesCommand(Key, "ev-1"), Ct);
        var second = string.Join(";", _written.ShouldNotBeNull().Select(Show)) + "#" + _record.ShouldNotBeNull().NanCountsJson;

        second.ShouldBe(first);
    }

    private static string Show(ExtensionFeatureRow row) => row.TradeKey + "@" + row.DecisionAt.ToUnixTimeSeconds() + ":" + string.Join(",", row.Values.Select(v => BitConverter.DoubleToInt64Bits(v)));
}
