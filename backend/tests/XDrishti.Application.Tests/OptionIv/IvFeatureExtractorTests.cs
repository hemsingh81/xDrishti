#pragma warning disable CA2012 // NSubstitute setup on ValueTask members is not awaited
#pragma warning disable xUnit1051 // The NSubstitute matchers take Arg.Any<CancellationToken>(), not a token
using NSubstitute;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionIv;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.OptionIv;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Iv;
using XDrishti.Domain.Options.Quality;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Application.Tests.OptionIv;

/// <summary>
/// P14-11 through the extractor with fake ports: the bars are asked for at the seven offsets only and only for an option underlying, a trade whose instrument is not one reads nothing but the NIFTY series, the quality verdicts decide usability, and the archive watermark and the quality run ids are
/// collected for the record. The pure features are tested in the domain.
/// </summary>
public sealed class IvFeatureExtractorTests
{
    private static readonly Guid Share = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private static readonly Guid Nifty = Guid.Parse("00000000-0000-0000-0000-0000000000a3");

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly DateTimeOffset Decision = new(2026, 7, 29, 11, 0, 0, Ist);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly IRollingBarReader _bars = Substitute.For<IRollingBarReader>();

    private readonly IRollingQualityRuns _quality = Substitute.For<IRollingQualityRuns>();

    private readonly IOptionReference _reference = Substitute.For<IOptionReference>();

    private readonly IContractExpiryReader _contracts = Substitute.For<IContractExpiryReader>();

    private readonly IBarQueries _daily = Substitute.For<IBarQueries>();

    private readonly IAdjustmentFactors _factors = Substitute.For<IAdjustmentFactors>();

    private IvFeatureExtractor Extractor(bool shareHasSeries = true)
    {
        _reference.EpochsAsync(Ct).Returns(ValueTask.FromResult<IReadOnlyList<MarketEpoch>>([]));
        _reference.SymbolsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Ct).Returns(ValueTask.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [Share] = "NSE:RELIANCE", [Other] = "NSE:SLEEPY" }));
        _reference.FindUnderlyingAsync(ExpiryRules.Nifty, Ct).Returns(ValueTask.FromResult<Guid?>(Nifty));
        _bars.HasSeriesAsync(Share, Ct).Returns(ValueTask.FromResult(shareHasSeries));
        _bars.HasSeriesAsync(Nifty, Ct).Returns(ValueTask.FromResult(true));
        _bars.HasSeriesAsync(Other, Ct).Returns(ValueTask.FromResult(false));
        _contracts.ExpiriesAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<ContractExpiry>>([new ContractExpiry(new DateOnly(2026, 8, 25), 'M')]));
        _daily.ReadDailyBarsAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<BarRow>>([]));
        _factors.GetAsync(Arg.Any<Guid>(), Ct).Returns(XDrishti.Domain.Market.Actions.AdjustmentFactors.None);
        _bars.WatermarkAsync(Share, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult(40L));
        _bars.WatermarkAsync(Nifty, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult(55L));
        _bars.DailyLastBarsAsync(Arg.Any<Guid>(), Arg.Any<char>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<DailySideIv>>([]));
        _bars.BarsAsync(Share, 'M', 1, Arg.Any<string>(), 15, Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct)
            .Returns(ci => ValueTask.FromResult<IReadOnlyList<OptionBar>>([.. Enumerable.Range(-3, 7).Select(o => new OptionBar(new DateTimeOffset(2026, 7, 29, 10, 45, 0, Ist), o, 0, 0, 0, 0, 0, 0, 1, 100, o == 0 ? (ci.ArgAt<string>(3) == "CE" ? 15f : 17f) : 12f, false, false))]));
        _quality.RollingResultsForKeysAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct)
            .Returns(ci => ValueTask.FromResult<IReadOnlyList<RunQualityRow>>([.. ci.ArgAt<IReadOnlyCollection<string>>(1).SelectMany(key => new[]
            {
                new RunQualityRow(new OptionQualityRow(new DateOnly(2026, 7, 29), ci.ArgAt<Guid>(0), "rolling", null, key, new QualityCheck(QualityCodes.Coverage, QualityStatus.Pass, null, null, null, 1, null)), 7),
                new RunQualityRow(new OptionQualityRow(new DateOnly(2026, 7, 29), ci.ArgAt<Guid>(0), "rolling", null, key, new QualityCheck(QualityCodes.Stitching, QualityStatus.Pass, null, null, null, 1, null)), 9),
            })]));
        return new IvFeatureExtractor(_bars, _quality, _reference, _contracts, _daily, _factors);
    }

    private static ValueTask<IvExtraction> Run(IvFeatureExtractor extractor, params IvTrade[] trades) =>
        extractor.ExtractAsync(trades, EventCalendarSnapshot.Create([], 5), new TradingCalendar(), Ct);

    [Fact]
    public async Task A_share_with_a_series_gets_its_atm_iv_from_the_last_ended_bar_and_the_sources_name_the_watermark_and_the_quality_runs()
    {
        var extraction = await Run(Extractor(), new IvTrade(Share, "t1", Decision));

        var cells = extraction.Cells["t1"];
        cells.Length.ShouldBe(IvFeatureCatalog.Names.Count);
        cells[0].Value.ShouldBe(0.16, 1e-6);
        cells[3].Reason.ShouldBe(IvReasons.NoTermSeries, "a share has one series");
        cells[6].Value.ShouldBe(1.0, 1e-12, "equal open interest on both sides");
        extraction.Sources.ArchiveWatermark.ShouldBe(55, "the highest over the share and NIFTY");
        extraction.Sources.QualityRunIds.ShouldBe([7, 9]);
        extraction.Sources.IvUnit.ShouldBe("percent_assumed");
    }

    [Fact]
    public async Task The_bars_are_read_at_the_seven_offsets_only_through_the_offset_overload_and_never_the_unfiltered_one()
    {
        await Run(Extractor(), new IvTrade(Share, "t1", Decision));

        var offsets = _bars.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "BarsAsync").ToList();
        offsets.Count.ShouldBe(2, "CE and PE of the one held code");
        foreach (var call in offsets)
        {
            call.GetArguments()[5].ShouldBeAssignableTo<IReadOnlyCollection<int>>().ShouldBe([-3, -2, -1, 0, 1, 2, 3]);
        }
    }

    [Fact]
    public async Task A_trade_whose_instrument_is_not_an_option_underlying_reads_no_bar_and_gets_no_options_but_the_nifty_rank_is_asked_for()
    {
        var extraction = await Run(Extractor(), new IvTrade(Other, "t2", Decision));

        var cells = extraction.Cells["t2"];
        foreach (var index in new[] { 0, 1, 2, 3, 4, 6, 7 })
        {
            cells[index].Reason.ShouldBe(IvReasons.NoOptions, IvFeatureCatalog.Names[index]);
        }

        cells[5].Reason.ShouldBe(FeatureReasons.ShortHistory, "NIFTY has no daily values in the fake archive");
        await _bars.DidNotReceive().BarsAsync(Other, Arg.Any<char>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct);
        await _bars.Received().DailyLastBarsAsync(Nifty, 'W', Arg.Any<IReadOnlyCollection<int>>(), 5, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct);
        await _daily.DidNotReceive().ReadDailyBarsAsync(Other, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct);
    }

    [Fact]
    public async Task A_key_session_without_a_passing_verdict_makes_the_bar_unusable_and_the_value_archive_unusable()
    {
        var extractor = Extractor();
        _quality.RollingResultsForKeysAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Ct).Returns(ValueTask.FromResult<IReadOnlyList<RunQualityRow>>([]));

        var cells = (await Run(extractor, new IvTrade(Share, "t1", Decision))).Cells["t1"];

        cells[0].Reason.ShouldBe(IvReasons.ArchiveUnusable);
        cells[6].Reason.ShouldBe(IvReasons.ArchiveUnusable);
    }

    [Fact]
    public async Task A_run_spanning_seven_years_reads_the_daily_series_in_consecutive_windows_each_within_the_reader_limit()
    {
        var extractor = Extractor();
        var early = new DateTimeOffset(2019, 7, 1, 11, 0, 0, Ist);

        await Run(extractor, new IvTrade(Share, "old", early), new IvTrade(Share, "new", Decision));

        var calls = _bars.ReceivedCalls().Where(c => c.GetMethodInfo().Name == "DailyLastBarsAsync" && Equals(c.GetArguments()[0], Share)).Select(c => ((DateTimeOffset)c.GetArguments()[4]!, (DateTimeOffset)c.GetArguments()[5]!)).ToList();
        calls.Count.ShouldBeGreaterThan(1);
        calls.ShouldAllBe(c => (c.Item2 - c.Item1).TotalDays <= IRollingBarReader.MaxDailyRangeDays && c.Item2 > c.Item1);
        for (var i = 1; i < calls.Count; i++)
        {
            calls[i].Item1.ShouldBe(calls[i - 1].Item2, "consecutive windows leave no session out");
        }

        calls[^1].Item2.ShouldBe(new DateTimeOffset(2026, 7, 30, 0, 0, 0, Ist));
    }

    [Fact]
    public async Task No_trades_read_nothing_and_a_nifty_trade_reuses_its_own_series_once()
    {
        var empty = await Run(Extractor());
        empty.Cells.ShouldBeEmpty();
        await _reference.DidNotReceiveWithAnyArgs().EpochsAsync(Ct);

        var both = await Run(Extractor(), new IvTrade(Nifty, "n1", Decision));

        both.Cells["n1"].Length.ShouldBe(8);
        await _bars.Received(1).DailyLastBarsAsync(Nifty, 'W', Arg.Any<IReadOnlyCollection<int>>(), 5, Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Ct);
    }
}
