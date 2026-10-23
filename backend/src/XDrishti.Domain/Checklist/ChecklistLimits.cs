using XDrishti.Domain.Common;

namespace XDrishti.Domain.Checklist;

/// <summary>System-wide numbers of the checks that are not part of an account's risk profile. <c>ResultsWithinDays</c> 3 and the correlation 0.7 are the design (doc 15, doc 16); the volume share is a stated assumption (spec P7-05). Build with <see cref="Create"/>, which refuses values that would make a check meaningless.</summary>
public sealed record ChecklistLimits
{
    private ChecklistLimits(int resultsWithinDays, double maxPctOfAverageDailyVolume, double maxCorrelation)
    {
        ResultsWithinDays = resultsWithinDays;
        MaxPctOfAverageDailyVolume = maxPctOfAverageDailyVolume;
        MaxCorrelation = maxCorrelation;
    }

    /// <summary>The defaults: 3 days, 1 %, 0.7.</summary>
    public static ChecklistLimits Default { get; } = new(3, 1.0, 0.70);

    /// <summary>Results this many days ahead or fewer fail.</summary>
    public int ResultsWithinDays { get; }

    /// <summary>Most the ticket's quantity may be of the average daily volume, percent (assumption: 1).</summary>
    public double MaxPctOfAverageDailyVolume { get; }

    /// <summary>Highest correlation with open trades and approved tickets (doc 15: 0.7).</summary>
    public double MaxCorrelation { get; }

    /// <summary>The problems of the values; empty when they can be used.</summary>
    public static IReadOnlyList<string> Problems(int resultsWithinDays, double maxPctOfAverageDailyVolume, double maxCorrelation)
    {
        var p = new List<string>();
        if (resultsWithinDays is < 0 or > 30)
        {
            p.Add("ResultsWithinDays must be 0 to 30");
        }

        if (!double.IsFinite(maxPctOfAverageDailyVolume) || maxPctOfAverageDailyVolume is <= 0 or > 100)
        {
            p.Add("MaxPctOfAverageDailyVolume must be above 0 and at most 100");
        }

        if (!double.IsFinite(maxCorrelation) || maxCorrelation is <= 0 or > 1)
        {
            p.Add("MaxCorrelation must be above 0 and at most 1");
        }

        return p;
    }

    /// <summary>Builds the limits, or says what is wrong.</summary>
    public static Result<ChecklistLimits> Create(int resultsWithinDays, double maxPctOfAverageDailyVolume, double maxCorrelation)
    {
        var problems = Problems(resultsWithinDays, maxPctOfAverageDailyVolume, maxCorrelation);
        return problems.Count == 0
            ? Result.Success(new ChecklistLimits(resultsWithinDays, maxPctOfAverageDailyVolume, maxCorrelation))
            : Result.Failure<ChecklistLimits>(Error.Validation("checklist.invalid_limits", string.Join("; ", problems)));
    }
}
