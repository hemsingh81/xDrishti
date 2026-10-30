using XDrishti.Domain.Execution.Limits;
using XDrishti.Execution.Domain;
using XDrishti.Execution.Domain.Limits;

namespace XDrishti.Execution.Tests.Limits;

/// <summary>
/// The hand example of spec P12-09 criterion 2: 6 orders a day, Rs 60,000 per order, Rs 150,000 per day, Rs 1,500 loss, 3 open positions, window 09:20 to 14:45, allow-list TATAPOWER, INFY, SBIN, products EQ_MIS; and an entry of
/// 120 TATAPOWER at 412.35 (Rs 49,482.00) with stop 405.10 and target 425.80, approved by the owner on 23 Oct before the open and triggered at 09:46 India time on Friday 23 Oct 2026.
/// </summary>
internal static class LimitsScene
{
    public static readonly Guid Account = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static readonly ExecutionLimits Sheet = new(6, 60_000m, 150_000m, 1_500m, 3, ["TATAPOWER", "INFY", "SBIN"], new TimeOnly(9, 20), new TimeOnly(14, 45), [LimitsCeilings.EquityIntraday], false);

    /// <summary>Both switches ON and every key at its default (P12-01's global caps are wider than the sheet).</summary>
    public static readonly AutoLiveSettings On = AutoLiveSettings.From(7, true, true, null);

    public static DateTimeOffset Ist(int hour, int minute, int second = 0, int millisecond = 0) => new(2026, 10, 23, hour, minute, second, millisecond, TimeSpan.FromMinutes(330));

    public static readonly DateTimeOffset Now = Ist(9, 46);

    public static AutoEntry Entry(int quantity = 120, decimal price = 412.35m, string symbol = "NSE:TATAPOWER") =>
        new(Account, Guid.Parse("44444444-4444-4444-4444-444444444444"), 4711, symbol, "strategy-v1", LimitsCeilings.EquityIntraday, false, ExecSide.Buy, quantity, quantity, price, 405.10m, 425.80m, "owner", Ist(7, 30), Now);

    /// <summary>Every prefetched fact passes: plan final, checks passed, ready, no break, no halt, unlocked, no exposure, no position open, no loss and no open risk.</summary>
    public static EntryFacts Facts() => new(false, [], true, true, false, false, true, false, 0, 0m, 0m);

    public static AutoFacts All(ExecutionLimits? sheet = null, LedgerDay? day = null, EntryFacts? facts = null) =>
        new(sheet ?? Sheet, day ?? new LedgerDay(0, 0m), facts ?? Facts(), true, new OwnerSeen(Now.AddMinutes(-2), false), Now.AddSeconds(-1));
}
