namespace XDrishti.Domain.Learning;

/// <summary>The tier rule (BK-11): terciles by rank of the 20-session median traded value among the eligible shares, boundaries ceil(N/3) and ceil(2N/3).</summary>
/// <param name="MinBars">A share needs at least this many daily bars before the date to be ranked.</param>
/// <param name="MinEligible">Fewer eligible shares than this and every ranked share is <see cref="UniverseTier.Unavailable"/>.</param>
public sealed record UniverseTierOptions(int MinBars = 60, int MinEligible = 30)
{
    /// <summary>The problems of the values.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (MinBars < 20)
        {
            problems.Add("minBars is at least 20 (the median needs 20 sessions)");
        }

        if (MinEligible < 3)
        {
            problems.Add("minEligible is at least 3");
        }

        return problems;
    }
}
