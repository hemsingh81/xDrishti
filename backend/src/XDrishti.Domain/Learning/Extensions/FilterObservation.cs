using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Learning.Extensions;

/// <summary>One trade of an unfiltered stored run with the calendar's answer at its decision instant (P15-10, protocol F). The answer does not depend on the window; <see cref="ToTrade"/> classifies it for a window of N sessions with the one rule of the filter (<see cref="ResultsWindow"/>), so the report and the simulator cannot disagree.</summary>
/// <param name="Key">The trade key.</param>
/// <param name="Date">The India date of the entry.</param>
/// <param name="R">The gross result in R.</param>
/// <param name="Answer">The results answer at the decision instant.</param>
public sealed record FilterObservation(string Key, DateOnly Date, double R, ResultsAnswer Answer)
{
    /// <summary>Skipped when the next results event is within <paramref name="days"/> sessions, taken when it is further, unavailable (with the reason) when the calendar cannot say.</summary>
    public FilterTrade ToTrade(int days) => ResultsWindow.Classify(Answer, days) switch
    {
        WindowOutcome.Inside => new FilterTrade(Key, Date, R, FilterFate.Skipped),
        WindowOutcome.Outside => new FilterTrade(Key, Date, R, FilterFate.Taken),
        _ => new FilterTrade(Key, Date, R, FilterFate.Unavailable, Answer.Reason),
    };
}
