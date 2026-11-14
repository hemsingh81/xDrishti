using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// One date-driven feature (P3-01c): a number that changes at most once per India date, read as an indicator so that the frame can treat it like the others. It is fed a closed 1-minute bar at a time, but only the
/// <b>first bar of each India date</b> matters: a bar of the date it already holds changes nothing, so the value stays the same for the whole date. <see cref="FeatureFrame"/> feeds its date lane only at the first accepted
/// step of each date, which is also when it writes the feature's history (one entry per date). The NIFTY 50 / INDIA VIX context features were dropped (ADR 0291).
/// <para>
/// <see cref="DateFeatureKind.Weekday"/> is ready from the first bar; <see cref="DateFeatureKind.ExpiryDay"/> from the first bar once it has been given a set of dates (an empty set is a valid set: always 0). A date outside
/// the range of <see cref="DateOnly"/> (a garbage epoch) has no expiry day and no stored number: those values are not ready, and the weekday is still computed from the day number.
/// </para>
/// <para>Thread-confined like every indicator.</para>
/// </summary>
public sealed class DateFeature : IIndicator
{
    private readonly DateFeatureKind _kind;

    private readonly IReadOnlySet<DateOnly>? _expiryDays;

    private readonly IReadOnlyDictionary<DateOnly, ResultsDayValues>? _resultsDays;

    private readonly RelativeStrengthDays? _relativeStrength;

    private readonly int _p1;

    private readonly int _p2;

    private readonly int _p3;

    private readonly int _warmUp;

    private long _day;

    private bool _started;

    private double _value = double.NaN;

    /// <param name="kind">Which feature this instance reports.</param>
    /// <param name="expiryDays">The India dates that are expiry days, for <see cref="DateFeatureKind.ExpiryDay"/>; none gives a feature that is never ready. The set is read as given, so it must not change while the
    /// feature is in use (<see cref="FeatureFrame"/> takes its own copy).</param>
    /// <param name="resultsDays">The per-date results numbers of the instrument, for <see cref="DateFeatureKind.DaysToResults"/> and <see cref="DateFeatureKind.DaysSinceResults"/>; none, or a date it lacks, reads NaN.</param>
    /// <param name="relativeStrength">The stored relative-strength rows of the instrument, for <see cref="DateFeatureKind.MomRank"/>, <see cref="DateFeatureKind.RsVsIndex"/> and <see cref="DateFeatureKind.SectorRs"/> (BK-09); none reads NaN.</param>
    /// <param name="p1">The first parameter of the feature spec (the basket code, the window).</param>
    /// <param name="p2">The second parameter of the feature spec (the lookback).</param>
    /// <param name="p3">The third parameter of the feature spec (the skip).</param>
    public DateFeature(DateFeatureKind kind, IReadOnlySet<DateOnly>? expiryDays = null, IReadOnlyDictionary<DateOnly, ResultsDayValues>? resultsDays = null, RelativeStrengthDays? relativeStrength = null, int p1 = 0, int p2 = 0, int p3 = 0)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a known date feature.");
        }

        _kind = kind;
        _expiryDays = expiryDays;
        _resultsDays = resultsDays;
        _relativeStrength = relativeStrength;
        _p1 = p1;
        _p2 = p2;
        _p3 = p3;
        _warmUp = 1;
    }

    /// <summary>Which feature this instance reports.</summary>
    public DateFeatureKind Kind => _kind;

    public double Value => _value;

    public bool IsReady => !double.IsNaN(_value);

    public int WarmUp => _warmUp;

    /// <summary>Takes the value for the India date of the bar when it is the first bar of a new date; a bar of the date already held changes nothing.</summary>
    public void Update(in Bar bar)
    {
        var day = IndiaDays.DayOf(bar.EpochSeconds);
        if (_started && day == _day)
        {
            return;
        }

        _started = true;
        _day = day;
        _value = Read(day);
    }

    public void Reset()
    {
        _day = 0;
        _started = false;
        _value = double.NaN;
    }

    /// <summary>The ISO weekday (Monday 1 … Sunday 7) of an India day number; 1970-01-01, day 0, was a Thursday.</summary>
    private static double IsoWeekday(long day) => IndiaDays.DaysSinceMonday(day) + 1;

    private double Read(long day)
    {
        if (_kind == DateFeatureKind.Weekday)
        {
            return IsoWeekday(day);
        }

        if (!IndiaDays.TryDateOf(day, out var date))
        {
            return double.NaN;
        }

        if (_kind == DateFeatureKind.ExpiryDay)
        {
            return _expiryDays is null ? double.NaN : _expiryDays.Contains(date) ? 1.0 : 0.0;
        }

        if (_kind is DateFeatureKind.MomRank or DateFeatureKind.RsVsIndex or DateFeatureKind.SectorRs)
        {
            // the newest stored row strictly before the date: the previous closed session, never the date itself (no look-ahead, and no second shift on top of the as_of of the row)
            return _relativeStrength?.Before(_kind, _p1, _p2, _p3, date) ?? double.NaN;
        }

        if (_kind is DateFeatureKind.DaysToResults or DateFeatureKind.DaysSinceResults or DateFeatureKind.ResultsSueQuintile or DateFeatureKind.ResultsCar3Quintile)
        {
            return _resultsDays is not null && _resultsDays.TryGetValue(date, out var results)
                ? _kind switch
                {
                    DateFeatureKind.DaysToResults => results.DaysToResults,
                    DateFeatureKind.DaysSinceResults => results.DaysSinceResults,
                    DateFeatureKind.ResultsSueQuintile => results.SueQuintile,
                    _ => results.Car3Quintile,
                }
                : double.NaN;
        }

        return double.NaN;
    }
}
