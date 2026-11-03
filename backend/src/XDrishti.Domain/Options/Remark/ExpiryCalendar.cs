using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// Expiry dates of one underlying (spec P14-08). The rolling bars carry no expiry date, so a date comes from <c>options.contracts</c> when it holds that underlying, kind and period (label <c>expiry: contract</c>), else from the derived weekday rule of
/// <see cref="ExpiryRules"/> (label <c>expiry: derived</c>, unconfirmed): the nominal weekday of the week, or the last such weekday of the month, moved back to the previous trading day while it is not one. A date the rule cannot give is null: the caller
/// counts <c>expiry_unknown</c>. Pure.
/// </summary>
public sealed class ExpiryCalendar
{
    private const int MaxHorizonPeriods = 12;
    private const int HolidayLookbackDays = 14;
    private const int ContractMatchDays = 3;

    private readonly string _symbol;
    private readonly List<ContractExpiry> _contracts;
    private readonly List<MarketEpoch> _epochs;
    private readonly TradingCalendar _calendar;

    public ExpiryCalendar(string canonicalSymbol, IEnumerable<ContractExpiry> contracts, IEnumerable<MarketEpoch> epochs, TradingCalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(canonicalSymbol);
        ArgumentNullException.ThrowIfNull(contracts);
        ArgumentNullException.ThrowIfNull(epochs);
        ArgumentNullException.ThrowIfNull(calendar);
        _symbol = canonicalSymbol;
        _contracts = [.. contracts.Distinct().OrderBy(c => c.Date)];
        _epochs = [.. epochs];
        _calendar = calendar;
    }

    /// <summary>The <paramref name="code"/>-th (1 is the nearest) expiry of the kind on or after <paramref name="from"/>; null when fewer can be given.</summary>
    public ExpiryDate? Nth(char flag, DateOnly from, int code)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(code, 1);
        var merged = new SortedDictionary<DateOnly, ExpiryDate>();
        var matched = new HashSet<DateOnly>();
        foreach (var derived in Derived(flag, from, code))
        {
            var contract = _contracts.FirstOrDefault(c => c.Flag == flag && Math.Abs(c.Date.DayNumber - derived.DayNumber) <= ContractMatchDays);
            if (contract is not null)
            {
                matched.Add(contract.Date);
            }

            var chosen = contract?.Date ?? derived;
            if (chosen >= from)
            {
                merged[chosen] = new ExpiryDate(chosen, contract is not null);
            }
        }

        foreach (var contract in _contracts.Where(c => c.Flag == flag && c.Date >= from && !matched.Contains(c.Date)))
        {
            merged.TryAdd(contract.Date, new ExpiryDate(contract.Date, true));
        }

        return merged.Count >= code ? merged.Values.ElementAt(code - 1) : null;
    }

    /// <summary>The last trading day before the expiry date; null when none is found within two weeks.</summary>
    public DateOnly? LastSessionBefore(DateOnly expiry)
    {
        for (var back = 1; back <= HolidayLookbackDays; back++)
        {
            var date = expiry.AddDays(-back);
            if (_calendar.IsTradingDay(date))
            {
                return date;
            }
        }

        return null;
    }

    private List<DateOnly> Derived(char flag, DateOnly from, int code)
    {
        var dates = new List<DateOnly>();
        var eras = ExpiryRules.Eras(_symbol, flag, _epochs);
        if (eras.Count == 0)
        {
            return dates;
        }

        for (var step = 0; step < code + MaxHorizonPeriods; step++)
        {
            foreach (var era in eras)
            {
                DateOnly? nominal;
                if (flag == 'W')
                {
                    var monday = from.AddDays(-(((int)from.DayOfWeek + 6) % 7)).AddDays(7 * step);
                    nominal = monday.AddDays(((int)era.Day + 6) % 7);
                }
                else
                {
                    var month = new DateOnly(from.Year, from.Month, 1).AddMonths(step);
                    var last = new DateOnly(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month));
                    nominal = last.AddDays(-(((int)last.DayOfWeek - (int)era.Day + 7) % 7));
                }

                if (!era.Covers(nominal.Value))
                {
                    continue;
                }

                var adjusted = nominal.Value;
                for (var back = 0; back < HolidayLookbackDays && !_calendar.IsTradingDay(adjusted); back++)
                {
                    adjusted = adjusted.AddDays(-1);
                }

                if (_calendar.IsTradingDay(adjusted))
                {
                    dates.Add(adjusted);
                }

                break;
            }
        }

        return dates;
    }
}
