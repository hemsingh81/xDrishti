using XDrishti.Domain.Filings;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>Everything <see cref="FundamentalFeatures.Compute"/> may read for one decision (P15-05). The facts and the shareholding are answers <b>as of</b> the decision instant; the closes are raw daily closes of the instrument.</summary>
/// <param name="At">The decision instant.</param>
/// <param name="Lag">The lag between a broadcast and the moment it counts as known (used for the visibility of quarters in the earlier P/E values).</param>
/// <param name="Calendar">The trading calendar.</param>
/// <param name="Facts">The result facts visible at <see cref="At"/>, with the parser version they were read for.</param>
/// <param name="Shareholding">The shareholding visible at <see cref="At"/>.</param>
/// <param name="Actions">The applied splits and bonuses of the instrument.</param>
/// <param name="Closes">The raw daily closes of the instrument, ascending by date.</param>
public sealed record FundamentalFeatureInput(
    DateTimeOffset At,
    TimeSpan Lag,
    TradingCalendar Calendar,
    ResultFactsAnswer Facts,
    ShareholdingAnswer Shareholding,
    IReadOnlyList<AppliedAction> Actions,
    IReadOnlyList<DailyClose> Closes);
