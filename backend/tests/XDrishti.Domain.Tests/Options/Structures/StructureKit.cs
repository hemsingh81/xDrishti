using XDrishti.Domain.Learning;
using XDrishti.Domain.Options.Remark;
using XDrishti.Domain.Options.Structures;
using XDrishti.Domain.Tests.Options.Remark;

namespace XDrishti.Domain.Tests.Options.Structures;

/// <summary>Synthetic rolling series for the P14-10 tests: the premium of every offset at every bar is given by a function, the strike of an offset is the base spot plus offset steps (the spot never moves), so a hand case can set the premiums exactly. No figure is real.</summary>
internal static class StructureKit
{
    public static readonly DateOnly Wednesday = new(2025, 6, 11);

    public static SpreadParams Debit(string strike = "atm", int width = 2, decimal? stop = null) => SpreadParams.Create(Expression.DebitSpread, strike, width, "nearest_safe", stop, 1).Value;

    public static SpreadParams Credit(string strike = "atm", int width = 2) => SpreadParams.Create(Expression.CreditSpread, strike, width, "nearest_safe", null, 1).Value;

    /// <summary>Every bar of one day for the offsets -window..+window; open is <paramref name="prem"/>(bar, offset), close the next bar's open, high and low 50 paise outside.</summary>
    public static List<OptionBar> Bars(DateOnly day, Func<int, int, int> prem, int window = 10, float? iv = 15f, Func<int, int, OptionBar, OptionBar?>? tweak = null)
    {
        var bars = new List<OptionBar>();
        const int count = 375 / 5;
        for (var i = 0; i < count; i++)
        {
            var ts = RemarkKit.At(day, 9, 15).AddMinutes(i * 5);
            for (var offset = -window; offset <= window; offset++)
            {
                var open = prem(i, offset);
                var close = prem(Math.Min(i + 1, count - 1), offset);
                var bar = new OptionBar(ts, offset, RemarkKit.BaseSpot + (offset * RemarkKit.Step), RemarkKit.BaseSpot, open, Math.Max(open, close) + 50, Math.Max(Math.Min(open, close) - 50, 1), close, 1000, 10_000, iv, false, true);
                var changed = tweak is null ? bar : tweak(i, offset, bar);
                if (changed is not null)
                {
                    bars.Add(changed);
                }
            }
        }

        return bars;
    }

    /// <summary>A premium that falls with the distance from the money: 60 rupees at the money, 15 less per step, never under 3 rupees.</summary>
    public static int Decay(int bar, int offset) => Math.Max(6000 - (1500 * Math.Abs(offset)), 300);

    public static StructureRow Spread(RemarkTrade trade, IEnumerable<OptionBar> bars, SpreadParams profile, RemarkContext? context = null, SeriesPlan? plan = null) =>
        StructureRemark.EvaluateSpread(trade, plan ?? RemarkKit.Plan, profile, BarGrid.Build(bars), context ?? RemarkKit.Context());

    /// <summary>The entry and exit of a condor: 09:30 on the Tuesday, leaving after the Wednesday session (the close of its last bar).</summary>
    public static RemarkTrade CondorTrade(bool net = false) => new(
        "condor:2025-06-10:w1", true, RemarkKit.Tuesday, RemarkKit.Epoch(RemarkKit.Tuesday, 9, 30), RemarkKit.Epoch(Wednesday.AddDays(1), 0, 0), RemarkFates.ExpiryExit, 0m, 0m, 1m, true, 1m, 0d, net, false);

    public static List<OptionBar> TwoDays(Func<int, int, int> prem, int window = 10, Func<int, int, OptionBar, OptionBar?>? tweak = null) =>
        [.. Bars(RemarkKit.Tuesday, prem, window, tweak: tweak), .. Bars(Wednesday, prem, window, tweak: tweak == null ? null : (i, o, b) => tweak(i + 75, o, b))];
}
