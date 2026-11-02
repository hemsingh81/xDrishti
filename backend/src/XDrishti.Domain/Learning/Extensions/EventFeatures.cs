using XDrishti.Domain.Filings;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>
/// The features of <c>ev-1</c> at a decision instant (P15-05, ADR 0185, pure). Only what is public at the instant is read: calendar rows and filings with <c>time + lag &lt;= t</c>, and closes of sessions that have closed by <c>t</c>. Everything else is NaN with a closed reason from
/// <see cref="FeatureReasons"/>; nothing is filled, defaulted or taken from a neighbour.
/// </summary>
public static class EventFeatures
{
    private const int StepBackLimitDays = 30;

    public static FeatureCell[] Compute(EventFeatureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var visible = input.Filings.Where(f => f.BroadcastTs + input.Lag <= input.At).OrderByDescending(f => f.BroadcastTs.UtcTicks).ThenByDescending(f => f.PeriodEnd).ToList();
        return [DaysToResults(input), DaysSince(input, visible), Car3(input, visible)];
    }

    private static FeatureCell DaysToResults(EventFeatureInput input)
    {
        var answer = ResultsDays.Compute(input.CalendarRows, input.At, input.Lag, input.Calendar);
        return answer.State == ResultsAnswer.Value && answer.Days is { } days ? FeatureCell.Known(days) : FeatureCell.Unknown(FeatureReasons.NoCalendar);
    }

    private static FeatureCell DaysSince(EventFeatureInput input, List<ResultsFilingTime> visible)
    {
        if (visible.Count == 0)
        {
            return FeatureCell.Unknown(FeatureReasons.NoFiling);
        }

        if (ReactionSession.Of(visible[0].BroadcastTs, input.Lag, input.Calendar) is not { } reaction)
        {
            return FeatureCell.Unknown(FeatureReasons.NoCalendar);
        }

        var today = IndiaTime.Today(input.At);
        return reaction > today ? FeatureCell.Unknown(FeatureReasons.NoFiling) : FeatureCell.Known(input.Calendar.TradingDaysBetween(reaction, today));
    }

    // The three sessions ending at the close of R: close(R) / close(R-3) - 1 against the same for the index. The reaction close must have passed.
    private static FeatureCell Car3(EventFeatureInput input, List<ResultsFilingTime> visible)
    {
        if (visible.Count == 0)
        {
            return FeatureCell.Unknown(FeatureReasons.NoFiling);
        }

        DateOnly? reaction = null;
        foreach (var filing in visible)
        {
            if (ReactionSession.Of(filing.BroadcastTs, input.Lag, input.Calendar) is { } r && ReactionSession.CloseOf(r, input.Calendar) is { } close && close <= input.At)
            {
                reaction = r;
                break;
            }
        }

        if (reaction is not { } session)
        {
            return FeatureCell.Unknown(FeatureReasons.NoFiling);
        }

        var start = session;
        for (var i = 0; i < 3; i++)
        {
            if (PreviousSession(start, input) is not { } earlier)
            {
                return FeatureCell.Unknown(FeatureReasons.NoCalendar);
            }

            start = earlier;
        }

        if (!input.StockCloses.TryGetValue(session, out var stockEnd) || !input.StockCloses.TryGetValue(start, out var stockStart) || stockEnd <= 0 || stockStart <= 0)
        {
            return FeatureCell.Unknown(FeatureReasons.NoBars);
        }

        if (input.IndexCloses is null || !input.IndexCloses.TryGetValue(session, out var indexEnd) || !input.IndexCloses.TryGetValue(start, out var indexStart) || indexEnd <= 0 || indexStart <= 0)
        {
            return FeatureCell.Unknown(FeatureReasons.NoContext);
        }

        // an action inside the window changes the raw ratio; the adjusted close of the start session is the raw one times the factors of the actions after it up to R (those after R cancel in the ratio and are not read)
        var stock = (stockEnd / (stockStart * EpsAdjustment.Factor(input.Actions, start, session))) - 1.0;
        var index = (indexEnd / indexStart) - 1.0;
        return FeatureCell.Known(stock - index);
    }

    private static DateOnly? PreviousSession(DateOnly date, EventFeatureInput input)
    {
        for (var i = 1; i <= StepBackLimitDays; i++)
        {
            var candidate = date.AddDays(-i);
            if (input.Calendar.IsTradingDay(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
