using System.Globalization;

namespace XDrishti.Domain.Shadow;

/// <summary>
/// The edge count (spec CF-08, D5, criteria 8 and 9): the graded shadow trades that count for edge, raw and as an effective n. Candidates of one day are correlated, so a day with <c>m</c> candidates and average same-day correlation <c>rho</c> is worth
/// its graded trades divided by the design effect <c>1 + (m - 1) * rho</c>; a day with no candidate contributes nothing. It is its own count: it never feeds the P10 count or its exit criterion. Pure.
/// </summary>
/// <param name="RawN">Graded trades that count for edge.</param>
/// <param name="EffectiveN">The effective n.</param>
/// <param name="Rho">The correlation used.</param>
/// <param name="RhoIsPlaceholder">Whether <see cref="Rho"/> is the placeholder (CF-01 has not estimated it).</param>
/// <param name="Sessions">Sessions that had at least one candidate.</param>
public sealed record EdgeCount(int RawN, double EffectiveN, double Rho, bool RhoIsPlaceholder, int Sessions)
{
    /// <summary>The placeholder correlation until CF-01 estimates it.</summary>
    public const double PlaceholderRho = 0.3;

    /// <summary>The design effect of a day with <paramref name="candidates"/> candidates.</summary>
    public static double DesignEffect(int candidates, double rho) => 1.0 + ((candidates - 1) * rho);

    /// <summary>The edge count of the days; <paramref name="rho"/> null uses the placeholder.</summary>
    public static EdgeCount Compute(IEnumerable<EdgeDay> days, double? rho = null)
    {
        ArgumentNullException.ThrowIfNull(days);
        var used = rho ?? PlaceholderRho;
        if (!double.IsFinite(used) || used < 0 || used > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rho), "The correlation must be between 0 and 1.");
        }

        int raw = 0, sessions = 0;
        double effective = 0;
        foreach (var day in days)
        {
            if (day.Candidates <= 0)
            {
                continue;
            }

            sessions++;
            raw += day.Graded;
            effective += day.Graded / DesignEffect(day.Candidates, used);
        }

        return new EdgeCount(raw, effective, used, rho is null, sessions);
    }

    /// <summary>The sessions needed to reach <paramref name="target"/> at <paramref name="perSession"/> a session, rounded up; null when the rate is not above zero.</summary>
    public static int? SessionsToReach(double target, double perSession) =>
        perSession > 0 && double.IsFinite(perSession) ? (int)Math.Ceiling((target / perSession) - 1e-9) : null;

    /// <summary>The fewest graded trades an ETA from the observed rate needs (as the P10 review).</summary>
    public const int EtaMinTrades = 10;

    /// <summary>
    /// The sessions still needed to reach <paramref name="target"/> effective trades at the rate seen so far, a straight-line guess; null while there are fewer than <see cref="EtaMinTrades"/> graded trades or no session; 0 when reached.
    /// </summary>
    public int? EtaSessions(double target)
    {
        if (RawN < EtaMinTrades || Sessions <= 0)
        {
            return null;
        }

        return EffectiveN >= target ? 0 : SessionsToReach(target - EffectiveN, EffectiveN / Sessions);
    }

    /// <summary>One line for the summary: raw n, effective n, the correlation and whether it is the placeholder.</summary>
    public string Line() =>
        string.Create(CultureInfo.InvariantCulture, $"raw n {RawN}, effective n {EffectiveN:0.0}, rho {Rho:0.00}") + (RhoIsPlaceholder ? " (" + ShadowWording.PlaceholderRho + ")" : string.Empty);
}
