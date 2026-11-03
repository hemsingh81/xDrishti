namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// The derived expiry weekday table (spec P14-08, ADR 0197): <b>secondary-source data, UNVERIFIED</b> until it is checked against the NSE circulars. NIFTY weekly and monthly: Thursday until 2025-08-31, Tuesday from 2025-09-01 (epoch <c>nifty_expiry_tuesday</c>).
/// BANKNIFTY weekly: Thursday until 2023-09-05, Wednesday from 2023-09-06 until the day before 2024-11-20 (epoch <c>weekly_index_one_per_exchange</c>, when its weeklies were withdrawn); BANKNIFTY monthly: the last Thursday, then the last Tuesday
/// from 2025-09-01. Shares: monthly only, the last Thursday, then the last Tuesday. A date or an underlying the table does not cover gives no derived expiry. The two epoch dates are taken from the stored epoch rows when present.
/// </summary>
public static class ExpiryRules
{
    public const string TuesdayEpoch = "nifty_expiry_tuesday";
    public const string WeeklyEpoch = "weekly_index_one_per_exchange";
    public const string Nifty = "NSE:NIFTY";
    public const string BankNifty = "NSE:BANKNIFTY";

    private static readonly DateOnly DefaultTuesdayFrom = new(2025, 9, 1);
    private static readonly DateOnly DefaultWeeklyEnd = new(2024, 11, 20);
    private static readonly DateOnly BankNiftyWednesdayFrom = new(2023, 9, 6);

    /// <summary>The eras of the underlying and kind (<c>W</c> or <c>M</c>), oldest first; empty when the table has none.</summary>
    public static IReadOnlyList<ExpiryEra> Eras(string canonicalSymbol, char flag, IEnumerable<MarketEpoch> epochs)
    {
        ArgumentNullException.ThrowIfNull(epochs);
        var list = epochs.ToList();
        var tuesday = list.FirstOrDefault(e => e.Code == TuesdayEpoch)?.EffectiveFrom ?? DefaultTuesdayFrom;
        var weeklyEnd = list.FirstOrDefault(e => e.Code == WeeklyEpoch)?.EffectiveFrom ?? DefaultWeeklyEnd;
        ExpiryEra[] thursdayThenTuesday = [new(null, tuesday.AddDays(-1), DayOfWeek.Thursday), new(tuesday, null, DayOfWeek.Tuesday)];
        if (flag == 'M')
        {
            return thursdayThenTuesday;
        }

        if (flag != 'W')
        {
            return [];
        }

        return canonicalSymbol switch
        {
            Nifty => thursdayThenTuesday,
            BankNifty => [new(null, BankNiftyWednesdayFrom.AddDays(-1), DayOfWeek.Thursday), new(BankNiftyWednesdayFrom, weeklyEnd.AddDays(-1), DayOfWeek.Wednesday)],
            _ => [],
        };
    }

    /// <summary>The date BANKNIFTY's weekly series ends at (a trade on or before it can use <c>W</c>).</summary>
    public static DateOnly BankNiftyWeeklyEnd(IEnumerable<MarketEpoch> epochs) =>
        epochs?.FirstOrDefault(e => e.Code == WeeklyEpoch)?.EffectiveFrom ?? DefaultWeeklyEnd;
}
