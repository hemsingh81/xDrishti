using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Options.Iv;

/// <summary>Everything <see cref="IvFeatures.Compute"/> may read for one decision (P14-11). The extractor hands over what it has; the function looks only at what is visible at <see cref="At"/>.</summary>
/// <param name="At">The decision instant.</param>
/// <param name="Calendar">The trading calendar.</param>
/// <param name="Plan">The series of the trade date; null when the instrument is not an option underlying or no series has enough sessions to expiry.</param>
/// <param name="Bars">The bars read for the instrument's series; null when it has none.</param>
/// <param name="OwnDaily">The instrument's daily ATM IV series; null when it is not an option underlying of the archive.</param>
/// <param name="NiftyDaily">The NIFTY daily ATM IV series; null when the archive holds none.</param>
/// <param name="Closes">The raw daily closes of the instrument by India date.</param>
/// <param name="Actions">The applied splits and bonuses of the instrument.</param>
/// <param name="CalendarRows">The stored results meeting rows of the instrument.</param>
/// <param name="Lag">The lag between an intimation and the moment it counts as known.</param>
public sealed record IvFeatureInput(
    DateTimeOffset At,
    TradingCalendar Calendar,
    IvSeriesPlan? Plan,
    IvBarBook? Bars,
    IReadOnlyDictionary<DateOnly, double>? OwnDaily,
    IReadOnlyDictionary<DateOnly, double>? NiftyDaily,
    IReadOnlyDictionary<DateOnly, double> Closes,
    IReadOnlyList<AppliedAction> Actions,
    IReadOnlyList<CalendarRow> CalendarRows,
    TimeSpan Lag);
