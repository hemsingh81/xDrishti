using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.OptionExpression;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Quality;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Application.Tests.OptionExpression;

/// <summary>
/// P14-08 criteria 1, 7, 9 and 10 (use case) on synthetic data: a cash run of 50 trades (30 on an underlying with series, 20 without) gives exactly 50 rows, 20 of them <c>no_series</c>; counts per reason are in the run row; a second run writes nothing; the profile and the run must exist;
/// a future profile is refused; the usability of a key-session comes from the stored quality verdicts, never assumed.
/// </summary>
public sealed class RemarkRunHandlerTests
{
    private static readonly Guid Nifty = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    private static readonly Guid Other = Guid.Parse("00000000-0000-7000-8000-0000000000b2");
    private static readonly string RunKey = new('a', 64);
    private static readonly DateOnly Monday = new(2025, 6, 2);
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);
    private static readonly int[] Codes = [1, 2];

    private readonly IRunStore _runs = Substitute.For<IRunStore>();
    private readonly IExpressionProfileStore _profiles = Substitute.For<IExpressionProfileStore>();
    private readonly ICostSheetStore _sheets = Substitute.For<ICostSheetStore>();
    private readonly IOptionReference _reference = Substitute.For<IOptionReference>();
    private readonly IOptionQualityStore _quality = Substitute.For<IOptionQualityStore>();
    private readonly IRollingBarReader _bars = Substitute.For<IRollingBarReader>();
    private readonly IContractExpiryReader _contracts = Substitute.For<IContractExpiryReader>();
    private readonly ISpreadModelStore _spreadModels = Substitute.For<ISpreadModelStore>();
    private readonly ISpreadModelSettings _spreadSettings = Substitute.For<ISpreadModelSettings>();
    private readonly IBarStore _minutes = Substitute.For<IBarStore>();
    private readonly ITradingCalendar _calendar = Substitute.For<ITradingCalendar>();
    private readonly IRemarkSettings _settings = Substitute.For<IRemarkSettings>();
    private readonly MemoryStore _store = new();
    private readonly ExpressionProfile _profile = OptionProfileParams.Create("atm", "nearest_safe", null, 1).Value.ToProfile().Value;

    public RemarkRunHandlerTests()
    {
        _profiles.GetAsync(_profile.Hash, Arg.Any<CancellationToken>()).Returns(new StoredExpressionProfile(_profile, DateTimeOffset.UnixEpoch));
        _calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        _reference.EpochsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _reference.SymbolsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, string> { [Nifty] = "NSE:NIFTY" });
        _reference.LotSizesAsync(Nifty, Arg.Any<CancellationToken>()).Returns([new LotSizeEntry(1, Nifty, new DateOnly(2024, 11, 20), 75, LotSizeEntry.Secondary, false)]);
        _contracts.ExpiriesAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _spreadModels.LatestAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HalfSpreadModel?)null);
        _spreadSettings.Liquidity.Returns(new LiquidityThresholds(5m, 25m));
        _spreadSettings.AssumedHalfSpreadPct(Arg.Any<string>()).Returns((decimal?)null);
        _minutes.ReadAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        _settings.MaxIvAgeMinutes.Returns(60);
        _bars.BarsAsync(Arg.Any<Guid>(), Arg.Any<char>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => Series(call.ArgAt<DateTimeOffset>(5), call.ArgAt<DateTimeOffset>(6), call.ArgAt<string>(3) == "CE"));
        _bars.WatermarkAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(42L);
        Usable(true);
    }

    private RemarkRunHandler Handler() => new(_runs, _profiles, _sheets, _reference, _quality, _bars, _contracts, _spreadModels, _spreadSettings, _minutes, _calendar, _settings, _store, NullLogger<RemarkRunHandler>.Instance);

    /// <summary>The stored verdicts: coverage and stitching pass for every offset, both series codes, each weekday of the range.</summary>
    private void Usable(bool pass) => _quality.RollingResultsAsync(Nifty, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
    {
        var rows = new List<OptionQualityRow>();
        if (!pass)
        {
            return (IReadOnlyList<OptionQualityRow>)rows;
        }

        for (var day = call.ArgAt<DateOnly>(1); day <= call.ArgAt<DateOnly>(2); day = day.AddDays(1))
        {
            foreach (var code in Codes)
            {
                for (var offset = -10; offset <= 10; offset++)
                {
                    var key = $"{Nifty}:W{code}:{offset}:CE:5";
                    rows.Add(new OptionQualityRow(day, Nifty, QualityCodes.Rolling, null, key, new QualityCheck(QualityCodes.Coverage, QualityStatus.Pass, null, null, null, 1, null)));
                    rows.Add(new OptionQualityRow(day, Nifty, QualityCodes.Rolling, null, key, new QualityCheck(QualityCodes.Stitching, QualityStatus.Pass, null, null, null, 1, null)));
                }
            }
        }

        return rows;
    });

    private static List<OptionBar> DayBars(DateOnly day)
    {
        var bars = new List<OptionBar>();
        for (var i = 0; i < 75; i++)
        {
            var ts = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 15)), India).AddMinutes(i * 5);
            for (var offset = -10; offset <= 10; offset++)
            {
                var strike = 2_200_000 + (offset * 5000);
                var premium = Math.Max(2_200_000 - strike, 0) + 6000;
                bars.Add(new OptionBar(ts, offset, strike, 2_200_000, premium, premium + 50, premium - 50, premium, 1000, 10_000, 15f, false, false));
            }
        }

        return bars;
    }

    private static List<OptionBar> Series(DateTimeOffset from, DateTimeOffset to, bool call)
    {
        var bars = new List<OptionBar>();
        if (call)
        {
            for (var day = DateOnly.FromDateTime(IndiaTime.ToIst(from).DateTime); day <= DateOnly.FromDateTime(IndiaTime.ToIst(to).DateTime); day = day.AddDays(1))
            {
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    bars.AddRange(DayBars(day));
                }
            }
        }

        return bars;
    }

    private static StoredRunHeader Header(string? sheetId = null, string? ratesSha = null) => new(
        1, RunKey, "sim-9", 1, 5, "ORB", new string('d', 64), Monday, Monday.AddDays(60), "list", "list", 2, 2, 50, 50, 50, 0, false, "{}", sheetId, ratesSha, 10, new string('e', 64), new string('f', 64), DateTimeOffset.UnixEpoch, "test");

    private void Run(IEnumerable<SimTrade> closed, IEnumerable<SimTrade>? open = null, StoredRunHeader? header = null)
    {
        _runs.FindAsync(RunKey, Arg.Any<CancellationToken>()).Returns(header ?? Header());
        _runs.StreamTradesAsync(RunKey, false, Arg.Any<CancellationToken>()).Returns(_ => Stream(closed));
        _runs.StreamTradesAsync(RunKey, true, Arg.Any<CancellationToken>()).Returns(_ => Stream(open ?? []));
    }

    private static async IAsyncEnumerable<RunTrade> Stream(IEnumerable<SimTrade> trades)
    {
        foreach (var trade in trades)
        {
            yield return new RunTrade(trade, []);
            await Task.Yield();
        }
    }

    private static SimTrade Cash(Guid instrument, string symbol, DateOnly date, ExitReason reason = ExitReason.Target)
    {
        var trade = FabricatedRun.Trade(instrument, symbol, Side.Buy, date, reason, entryMinute: 560, entry: 100, stop: 99, target: 102, exitPrice: 102, rExact: 2, rFilled: 2);
        return trade with { ExitEpochSeconds = Day.Epoch(date, 640) };
    }

    /// <summary>30 trades on the first 30 weekdays on NIFTY and 20 on an instrument with no series.</summary>
    private static List<SimTrade> Fifty()
    {
        var trades = new List<SimTrade>();
        var day = Monday;
        while (trades.Count < 30)
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                trades.Add(Cash(Nifty, "NSE:NIFTY", day));
            }

            day = day.AddDays(1);
        }

        trades.AddRange(Enumerable.Range(0, 20).Select(i => Cash(Other, "NSE:OTHER", Monday.AddDays(i))));
        return trades;
    }

    [Fact]
    public async Task Fifty_trades_give_fifty_rows_twenty_of_them_no_series_and_the_counts_in_the_run_row()
    {
        Run(Fifty());

        var result = await Handler().HandleAsync(new RemarkRunCommand(RunKey, _profile.Hash), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        var rows = _store.Rows;
        rows.Count.ShouldBe(50);
        rows.Select(r => r.TradeKey).Distinct().Count().ShouldBe(50);
        rows.Count(r => r.Fate == RemarkFates.NoSeries && r.Expiry is null).ShouldBe(20);
        result.Value.Trades.ShouldBe(50);
        result.Value.Unpaired[RemarkFates.NoSeries].ShouldBe(20);
        result.Value.Paired.ShouldBe(rows.Count(r => r.IsPaired));
        result.Value.Paired.ShouldBe(30);
        (result.Value.Paired + result.Value.Unpaired.Values.Sum()).ShouldBe(50);
        _store.Run!.Trades.ShouldBe(50);
        _store.Run.Paired.ShouldBe(30);
        _store.Run.UnpairedJson.ShouldContain("\"no_series\":20");
        _store.Run.Labels.ShouldContain("basis: gross");
        _store.Run.BsFallbackShare.ShouldBe(0.0);
        rows.Where(r => !r.IsPaired).ShouldAllBe(r => r.REquiv == null && r.EntryPremiumPx == null);
        foreach (var row in rows.Where(r => r.IsPaired))
        {
            // every cash entry is 09:20 on its own date (the last field of the key): the option bar is the one starting then; the cash exit is 10:40
            var date = DateOnly.ParseExact(row.TradeKey[^10..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            row.EntryTs.ShouldBe(new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 20)), India), row.TradeKey);
            row.ExitTs.ShouldBe(new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 40)), India), row.TradeKey);
            row.Labels.ShouldContain("planned_hold_sessions: 1 (1 = intraday)");
        }
    }

    [Fact]
    public async Task A_second_run_with_the_same_inputs_finds_the_run_stored_and_writes_nothing()
    {
        Run(Fifty());
        var first = await Handler().HandleAsync(new RemarkRunCommand(RunKey, _profile.Hash), TestContext.Current.CancellationToken);

        var second = await Handler().HandleAsync(new RemarkRunCommand(RunKey, _profile.Hash), TestContext.Current.CancellationToken);

        first.Value.Stored.ShouldBeTrue();
        second.Value.Stored.ShouldBeFalse();
        second.Value.RunId.ShouldBe(first.Value.RunId);
        _store.Appends.ShouldBe(1);
    }

    [Fact]
    public async Task The_run_id_does_not_depend_on_the_order_of_the_cash_trades_and_follows_the_archive_watermark()
    {
        Run(Fifty());
        var forward = await Handler().HandleAsync(new RemarkRunCommand(RunKey, _profile.Hash), TestContext.Current.CancellationToken);
        Run(Enumerable.Reverse(Fifty()));
        var reversed = await Handler().HandleAsync(new RemarkRunCommand(RunKey, _profile.Hash), TestContext.Current.CancellationToken);
        _bars.WatermarkAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(43L);
        var later = await Handler().HandleAsync(new RemarkRunCommand(RunKey, _profile.Hash), TestContext.Current.CancellationToken);

        reversed.Value.RunId.ShouldBe(forward.Value.RunId);
        later.Value.RunId.ShouldNotBe(forward.Value.RunId);
        _store.Run!.InputsJson.ShouldContain("\"archive_watermark\":43");
    }

    [Fact]
    public async Task Key_sessions_the_quality_checks_did_not_pass_are_never_read_as_prices_and_every_trade_is_archive_unusable()
    {
        Usable(false);
        Run(Fifty().Take(5));

        var result = await Handler().HandleAsync(new RemarkRunCommand(RunKey, _profile.Hash), TestContext.Current.CancellationToken);

        result.Value.Unpaired[RemarkFates.ArchiveUnusable].ShouldBe(5);
        result.Value.Paired.ShouldBe(0);
        result.Value.BsFallbackShare.ShouldBeNull();
    }

    [Fact]
    public async Task A_trade_still_open_at_the_end_is_counted_open_at_end_and_not_dropped()
    {
        Run(Fifty().Take(3), [Cash(Nifty, "NSE:NIFTY", Monday.AddDays(20))]);

        var result = await Handler().HandleAsync(new RemarkRunCommand(RunKey, _profile.Hash), TestContext.Current.CancellationToken);

        result.Value.Trades.ShouldBe(4);
        result.Value.Unpaired[RemarkFates.OpenAtEnd].ShouldBe(1);
    }

    [Fact]
    public async Task A_stored_sheet_that_no_longer_hashes_to_the_run_s_is_basis_mismatch_for_every_trade()
    {
        var sheet = RemarkSheet();
        _sheets.FindAsync("test-options-remark", Arg.Any<CancellationToken>()).Returns(Result.Success(sheet));
        Run(Fifty().Take(4), header: Header("test-options-remark", new string('0', 64)));

        var result = await Handler().HandleAsync(new RemarkRunCommand(RunKey, _profile.Hash), TestContext.Current.CancellationToken);

        result.Value.Unpaired[RemarkFates.BasisMismatch].ShouldBe(4);
        _store.Run!.Labels.ShouldContain("basis: net");
    }

    [Fact]
    public async Task A_run_or_a_profile_that_is_not_stored_and_a_future_profile_are_refused_before_anything_is_read()
    {
        Run(Fifty().Take(1));
        var future = ExpressionProfile.Create(Expression.Future, []).Value;
        _profiles.GetAsync(future.Hash, Arg.Any<CancellationToken>()).Returns(new StoredExpressionProfile(future, DateTimeOffset.UnixEpoch));

        var noProfile = await Handler().HandleAsync(new RemarkRunCommand(RunKey, new string('9', 64)), TestContext.Current.CancellationToken);
        var noRun = await Handler().HandleAsync(new RemarkRunCommand(new string('8', 64), _profile.Hash), TestContext.Current.CancellationToken);
        var noFutures = await Handler().HandleAsync(new RemarkRunCommand(RunKey, future.Hash), TestContext.Current.CancellationToken);

        noProfile.Error.Code.ShouldBe(RemarkRunHandler.ProfileNotFoundCode);
        noRun.Error.Code.ShouldBe(RemarkRunHandler.RunNotFoundCode);
        noFutures.Error.Code.ShouldBe(OptionProfileParams.NoFuturesBarsCode);
        _store.Appends.ShouldBe(0);
        await _bars.DidNotReceiveWithAnyArgs().BarsAsync(default, default, default, default!, default, default, default, TestContext.Current.CancellationToken);
    }

    private static CostSheet RemarkSheet() => TestCostSheet.Of(
        "test-options-remark",
        ChargeSchedule.Create(new DateOnly(2025, 1, 1), 18m, ChargeRounding.HalfAwayFromZero, TestCostSheet.IntradayA, TestCostSheet.DeliveryA, TestCostSheet.Rates(0m, 20m, 20m, 0m, 0.10m, 0.03m, 10m, 0.003m, 0m, 0m)).Value);

    private sealed class MemoryStore : IExpressionTradeStore
    {
        private readonly HashSet<string> _ids = [];

        public ExpressionRunRow? Run { get; private set; }

        public IReadOnlyList<RemarkRow> Rows { get; private set; } = [];

        public int Appends { get; private set; }

        public ValueTask<bool> ExistsAsync(string runId, CancellationToken cancellationToken) => ValueTask.FromResult(_ids.Contains(runId));

        public ValueTask<bool> AppendAsync(ExpressionRunRow run, IReadOnlyList<RemarkRow> rows, CancellationToken cancellationToken)
        {
            if (!_ids.Add(run.RunId))
            {
                return ValueTask.FromResult(false);
            }

            Appends++;
            Run = run;
            Rows = rows;
            return ValueTask.FromResult(true);
        }
    }
}
