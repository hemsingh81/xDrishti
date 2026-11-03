using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Costs;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Research;
using XDrishti.Tests.Shared;

namespace XDrishti.Domain.Tests.Options.Remark;

/// <summary>Synthetic rolling series, trades, plans and contexts for the P14-08 tests. No figure here is real: spots, premiums and rates are invented round numbers.</summary>
internal static class RemarkKit
{
    public const int Step = 5000;
    public const int BaseSpot = 2_200_000;

    public static readonly Guid Underlying = Guid.Parse("00000000-0000-7000-8000-0000000000a1");

    /// <summary>Tuesday; the nearest weekly expiry is the Thursday after.</summary>
    public static readonly DateOnly Tuesday = new(2025, 6, 10);

    public static readonly DateOnly Thursday = new(2025, 6, 12);

    public static readonly TradingCalendar Weekdays = new();

    public static SeriesPlan Plan { get; } = new('W', 1, new ExpiryDate(Thursday, false), 5, new DateOnly(2025, 6, 11));

    public static OptionProfileParams Atm { get; } = OptionProfileParams.Create("atm", "nearest_safe", null, 1).Value;

    public static OptionProfileParams WithStop(decimal pct) => OptionProfileParams.Create("atm", "nearest_safe", pct, 1).Value;

    public static DateTimeOffset At(DateOnly day, int hour, int minute) => new(day.ToDateTime(new TimeOnly(hour, minute)), IndiaTime.Offset);

    public static long Epoch(DateOnly day, int hour, int minute) => At(day, hour, minute).ToUnixTimeSeconds();

    public static int Prem(bool call, int spot, int strike) => (call ? Math.Max(spot - strike, 0) : Math.Max(strike - spot, 0)) + 6000;

    /// <summary>
    /// Every bar of one day (09:15 to the last bar before 15:30) for the offsets -window..+window: the strike of an offset is the ATM strike of the bar's spot plus offset steps, the premium is intrinsic plus 60 rupees, the open is the premium at the bar's spot and the close
    /// the premium at the next bar's spot. <paramref name="spotAt"/> gives the spot in paise from the bar number.
    /// </summary>
    public static List<OptionBar> Day(DateOnly day, bool call, Func<int, int> spotAt, int interval = 5, int window = 10, Func<OptionBar, OptionBar>? tweak = null, float? iv = 15f)
    {
        var bars = new List<OptionBar>();
        var count = (375 / interval);
        for (var i = 0; i < count; i++)
        {
            var ts = At(day, 9, 15).AddMinutes(i * interval);
            var spot = spotAt(i);
            var next = spotAt(Math.Min(i + 1, count - 1));
            var atm = (int)(Math.Round(spot / (double)Step) * Step);
            for (var offset = -window; offset <= window; offset++)
            {
                var strike = atm + (offset * Step);
                var open = Prem(call, spot, strike);
                var close = Prem(call, next, strike);
                var bar = new OptionBar(ts, offset, strike, spot, open, Math.Max(open, close) + 50, Math.Max(Math.Min(open, close) - 50, 1), close, 1000, 10_000, iv, false, true);
                bars.Add(tweak is null ? bar : tweak(bar));
            }
        }

        return bars;
    }

    public static int Flat(int bar) => BaseSpot;

    public static RemarkTrade Trade(string key = "S|A|Buy|2025-06-10", bool buy = true, DateOnly? day = null, int entryHour = 9, int entryMinute = 20, int exitHour = 10, int exitMinute = 0, decimal entry = 1000m, decimal stop = 990m, decimal quantity = 100m, string reason = "target", bool net = false, bool open = false)
    {
        var date = day ?? Tuesday;
        return new RemarkTrade(key, buy, date, Epoch(date, entryHour, entryMinute), Epoch(date, exitHour, exitMinute), reason, entry, stop, quantity, true, quantity * Math.Abs(entry - stop), 2.0, net, open);
    }

    public static RemarkContext Context(CostSheet? sheet = null, HalfSpreadModel? model = null, decimal? assumedPct = null, int maxIvAge = 60, Func<DateTimeOffset, int?>? spot = null, IReadOnlyList<LotSizeEntry>? lots = null, bool sheetChanged = false, LiquidityThresholds? liquidity = null) => new()
    {
        Calendar = Weekdays,
        UnderlyingId = Underlying,
        Lots = lots ?? [new LotSizeEntry(1, Underlying, new DateOnly(2024, 11, 20), 75, LotSizeEntry.Secondary, false)],
        SpreadModel = model,
        AssumedHalfSpreadPct = assumedPct,
        Liquidity = liquidity ?? new LiquidityThresholds(5m, 25m),
        Sheet = sheet,
        SheetChanged = sheetChanged,
        MaxIvAgeMinutes = maxIvAge,
        SpotPaiseBefore = spot ?? (_ => BaseSpot),
    };

    /// <summary>A TEST sheet with option lines (flat 20 per order, invented rates), in force from 2025-01-01.</summary>
    public static CostSheet OptionSheet { get; } = TestCostSheet.Of(
        "test-options-remark",
        ChargeSchedule.Create(new DateOnly(2025, 1, 1), 18m, ChargeRounding.HalfAwayFromZero, TestCostSheet.IntradayA, TestCostSheet.DeliveryA, TestCostSheet.Rates(0m, 20m, 20m, 0m, 0.10m, 0.03m, 10m, 0.003m, 0m, 0m)).Value);

    public static RemarkRow Evaluate(RemarkTrade trade, IEnumerable<OptionBar> bars, OptionProfileParams? profile = null, RemarkContext? context = null, SeriesPlan? plan = null) =>
        OptionRemark.Evaluate(trade, plan ?? Plan, profile ?? Atm, BarGrid.Build(bars), context ?? Context());
}
