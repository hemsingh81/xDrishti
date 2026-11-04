using NSubstitute;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.OptionExpression;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Quality;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;

namespace XDrishti.Application.Tests.OptionExpression;

/// <summary>The fakes both structure handlers (P14-10) are built on: a NIFTY with call and put series that decay with the distance from the money, a regime store that can be empty or hold chosen range sessions, a spread model of a chosen number of sessions, a memory store of the run and its legs.</summary>
internal sealed class StructureRig
{
    public static readonly Guid Nifty = Guid.Parse("00000000-0000-7000-8000-0000000000a1");
    public static readonly Guid Share = Guid.Parse("00000000-0000-7000-8000-0000000000b2");
    public static readonly string RunKey = new('a', 64);
    public static readonly DateOnly Monday = new(2025, 6, 2);
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);
    private static readonly int[] Codes = [1, 2];
    private static readonly string[] Sides = ["CE", "PE"];

    public IRunStore Runs { get; } = Substitute.For<IRunStore>();

    public IExpressionProfileStore Profiles { get; } = Substitute.For<IExpressionProfileStore>();

    public ICostSheetStore Sheets { get; } = Substitute.For<ICostSheetStore>();

    public IOptionReference Reference { get; } = Substitute.For<IOptionReference>();

    public IOptionQualityStore Quality { get; } = Substitute.For<IOptionQualityStore>();

    public IRollingBarReader Bars { get; } = Substitute.For<IRollingBarReader>();

    public IContractExpiryReader Contracts { get; } = Substitute.For<IContractExpiryReader>();

    public ISpreadModelStore SpreadModels { get; } = Substitute.For<ISpreadModelStore>();

    public ISpreadModelSettings SpreadSettings { get; } = Substitute.For<ISpreadModelSettings>();

    public IBarStore Minutes { get; } = Substitute.For<IBarStore>();

    public ITradingCalendar Calendar { get; } = Substitute.For<ITradingCalendar>();

    public IRemarkSettings Settings { get; } = Substitute.For<IRemarkSettings>();

    public IRegimeLabels Regime { get; } = Substitute.For<IRegimeLabels>();

    public MemoryStructureStore Store { get; } = new();

    public StructureRig()
    {
        Calendar.GetAsync(Arg.Any<CancellationToken>()).Returns(new TradingCalendar());
        Reference.EpochsAsync(Arg.Any<CancellationToken>()).Returns([]);
        Reference.SymbolsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(call => (IReadOnlyDictionary<Guid, string>)new Dictionary<Guid, string> { [Nifty] = "NSE:NIFTY", [Share] = "NSE:RELIANCE" }.Where(p => call.ArgAt<IReadOnlyCollection<Guid>>(0).Contains(p.Key)).ToDictionary());
        Reference.FindUnderlyingAsync("NSE:NIFTY", Arg.Any<CancellationToken>()).Returns(Nifty);
        Reference.LotSizesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([new LotSizeEntry(1, Nifty, new DateOnly(2024, 11, 20), 75, LotSizeEntry.Secondary, false)]);
        Contracts.ExpiriesAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        SpreadModels.LatestAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((HalfSpreadModel?)null);
        SpreadSettings.Liquidity.Returns(new LiquidityThresholds(5m, 25m));
        SpreadSettings.MinUsableSessions.Returns(60);
        SpreadSettings.AssumedHalfSpreadPct(Arg.Any<string>()).Returns((decimal?)null);
        Minutes.ReadAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        Settings.MaxIvAgeMinutes.Returns(60);
        Settings.CondorCostSheetId.Returns((string?)null);
        Regime.AnyAsync(Arg.Any<CancellationToken>()).Returns(false);
        Regime.RangeSessionsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new HashSet<DateOnly>());
        Bars.BarsAsync(Arg.Any<Guid>(), Arg.Any<char>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => Series(call.ArgAt<DateTimeOffset>(5), call.ArgAt<DateTimeOffset>(6), call.ArgAt<string>(3) == "CE"));
        Bars.HasSeriesAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        Bars.WatermarkAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(42L);
        Usable();
    }

    public RemarkDispatchHandler Dispatcher() => new(Profiles, new RemarkRunHandler(Runs, Profiles, Sheets, Reference, Quality, Bars, Contracts, SpreadModels, SpreadSettings, Minutes, Calendar, Settings, Store, Microsoft.Extensions.Logging.Abstractions.NullLogger<RemarkRunHandler>.Instance), Structures());

    public RemarkStructureHandler Structures() => new(Runs, Sheets, Reference, Quality, Bars, Contracts, SpreadModels, SpreadSettings, Minutes, Calendar, Settings, Regime, Store, Microsoft.Extensions.Logging.Abstractions.NullLogger<RemarkStructureHandler>.Instance);

    public SimulateCondorsHandler Condors() => new(Profiles, Sheets, Reference, Quality, Bars, Contracts, SpreadModels, SpreadSettings, Minutes, Calendar, Settings, Regime, Store, Microsoft.Extensions.Logging.Abstractions.NullLogger<SimulateCondorsHandler>.Instance);

    public void Register(ExpressionProfile profile) => Profiles.GetAsync(profile.Hash, Arg.Any<CancellationToken>()).Returns(new StoredExpressionProfile(profile, DateTimeOffset.UnixEpoch));

    public void Model(int sessions) => SpreadModels.LatestAsync(Nifty, Arg.Any<CancellationToken>()).Returns(new HalfSpreadModel("model", Nifty, sessions, new DateOnly(2025, 1, 1), new DateOnly(2025, 5, 30), [new SpreadBucketRow(SpreadBuckets.All, "0-2", SpreadBuckets.All, 100, 0.5), new SpreadBucketRow(SpreadBuckets.All, "3-7", SpreadBuckets.All, 100, 0.5)]));

    public void RangeOn(params DateOnly[] sessions) => Regime.RangeSessionsAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(new HashSet<DateOnly>(sessions));

    /// <summary>The stored verdicts: coverage and stitching pass for every offset, both series codes and both sides.</summary>
    public void Usable() => Quality.RollingResultsAsync(Nifty, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
    {
        var rows = new List<OptionQualityRow>();
        for (var day = call.ArgAt<DateOnly>(1); day <= call.ArgAt<DateOnly>(2); day = day.AddDays(1))
        {
            foreach (var code in Codes)
            {
                foreach (var side in Sides)
                {
                    for (var offset = -10; offset <= 10; offset++)
                    {
                        var key = $"{Nifty}:W{code}:{offset}:{side}:5";
                        rows.Add(new OptionQualityRow(day, Nifty, QualityCodes.Rolling, null, key, new QualityCheck(QualityCodes.Coverage, QualityStatus.Pass, null, null, null, 1, null)));
                        rows.Add(new OptionQualityRow(day, Nifty, QualityCodes.Rolling, null, key, new QualityCheck(QualityCodes.Stitching, QualityStatus.Pass, null, null, null, 1, null)));
                    }
                }
            }
        }

        return (IReadOnlyList<OptionQualityRow>)rows;
    });

    public void Run(IEnumerable<XDrishti.Domain.Research.SimTrade> closed)
    {
        Runs.FindAsync(RunKey, Arg.Any<CancellationToken>()).Returns(new StoredRunHeader(
            1, RunKey, "sim-9", 1, 5, "ORB", new string('d', 64), Monday, Monday.AddDays(60), "list", "list", 2, 2, 50, 50, 50, 0, false, "{}", null, null, 10, new string('e', 64), new string('f', 64), DateTimeOffset.UnixEpoch, "lab:full"));
        Runs.StreamTradesAsync(RunKey, false, Arg.Any<CancellationToken>()).Returns(_ => Stream(closed));
        Runs.StreamTradesAsync(RunKey, true, Arg.Any<CancellationToken>()).Returns(_ => Stream([]));
    }

    private static async IAsyncEnumerable<XDrishti.Domain.Research.RunTrade> Stream(IEnumerable<XDrishti.Domain.Research.SimTrade> trades)
    {
        foreach (var trade in trades)
        {
            yield return new XDrishti.Domain.Research.RunTrade(trade, []);
            await Task.Yield();
        }
    }

    /// <summary>A premium that falls with the distance from the money (60 rupees, 6 less per step, never under 3), plus the intrinsic value of an in-the-money strike. The spot never moves: the strike of offset k is 22,000 plus 50 k.</summary>
    public static int Premium(bool call, int offset) => Math.Max(6000 - (600 * Math.Abs(offset)), 300) + (50 * 100 * (call ? Math.Max(-offset, 0) : Math.Max(offset, 0)));

    private static List<OptionBar> DayBars(DateOnly day, bool call)
    {
        var bars = new List<OptionBar>();
        for (var i = 0; i < 75; i++)
        {
            var ts = new DateTimeOffset(day.ToDateTime(new TimeOnly(9, 15)), India).AddMinutes(i * 5);
            for (var offset = -10; offset <= 10; offset++)
            {
                var premium = Premium(call, offset);
                bars.Add(new OptionBar(ts, offset, 2_200_000 + (offset * 5000), 2_200_000, premium, premium + 50, premium - 50, premium, 1000, 10_000, 15f, false, false));
            }
        }

        return bars;
    }

    private static List<OptionBar> Series(DateTimeOffset from, DateTimeOffset to, bool call)
    {
        var bars = new List<OptionBar>();
        for (var day = DateOnly.FromDateTime(IndiaTime.ToIst(from).DateTime); day <= DateOnly.FromDateTime(IndiaTime.ToIst(to).DateTime); day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                bars.AddRange(DayBars(day, call));
            }
        }

        return bars;
    }

    public static XDrishti.Domain.Research.SimTrade Cash(Guid instrument, string symbol, DateOnly date, XDrishti.Domain.Research.Side side = XDrishti.Domain.Research.Side.Buy)
    {
        var trade = XDrishti.Tests.Shared.FabricatedRun.Trade(instrument, symbol, side, date, XDrishti.Domain.Research.ExitReason.Target, entryMinute: 560, entry: 100, stop: 99, target: 102, exitPrice: 102, rExact: 2, rFilled: 2);
        return trade with { ExitEpochSeconds = XDrishti.Tests.Shared.Day.Epoch(date, 640) };
    }

    /// <summary>Five trades on NIFTY on the weekdays from Monday 2025-06-02, buys and sells alternating.</summary>
    public static List<XDrishti.Domain.Research.SimTrade> Week() => [.. Enumerable.Range(0, 5).Select(i => Cash(Nifty, "NSE:NIFTY", Monday.AddDays(i), i % 2 == 0 ? XDrishti.Domain.Research.Side.Buy : XDrishti.Domain.Research.Side.Sell))];
}

/// <summary>A memory store that keeps the last run, its rows and its legs and refuses nothing.</summary>
internal sealed class MemoryStructureStore : IExpressionTradeStore
{
    private readonly HashSet<string> _ids = [];

    public ExpressionRunRow? Run { get; private set; }

    public IReadOnlyList<RemarkRow> Rows { get; private set; } = [];

    public IReadOnlyList<StructureLegRow> Legs { get; private set; } = [];

    public int Appends { get; private set; }

    public ValueTask<bool> ExistsAsync(string runId, CancellationToken cancellationToken) => ValueTask.FromResult(_ids.Contains(runId));

    public ValueTask<bool> AppendAsync(ExpressionRunRow run, IReadOnlyList<RemarkRow> rows, CancellationToken cancellationToken) => AppendAsync(run, rows, [], cancellationToken);

    public ValueTask<bool> AppendAsync(ExpressionRunRow run, IReadOnlyList<RemarkRow> rows, IReadOnlyList<StructureLegRow> legs, CancellationToken cancellationToken)
    {
        if (!_ids.Add(run.RunId))
        {
            return ValueTask.FromResult(false);
        }

        Appends++;
        Run = run;
        Rows = rows;
        Legs = legs;
        return ValueTask.FromResult(true);
    }
}
