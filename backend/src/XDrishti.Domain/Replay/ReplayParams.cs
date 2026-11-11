namespace XDrishti.Domain.Replay;

/// <summary>
/// What the owner chose for one replay (SR-01). <paramref name="CostSheetId"/> null means gross: the run is then labelled <c>before_costs</c> (ISS-25). The capital is the starting capital; later sessions size from the replay equity (Q5).
/// </summary>
/// <param name="From">The first session replayed (D).</param>
/// <param name="To">The last session replayed (E).</param>
/// <param name="Scope">Baskets and instruments covered.</param>
/// <param name="Capital">Starting capital in rupees.</param>
/// <param name="Profile">The risk limits of the replay account.</param>
/// <param name="CostSheetId">The stored charge sheet to net the trades with; null is gross.</param>
/// <param name="VersionRule">Start or as-of strategy versions.</param>
/// <param name="Mode">Frozen or learning.</param>
/// <param name="FiltersWithoutHistory">Off by default (the live behaviour). On: the event, F&amp;O-ban and surveillance filters, which have no history, do not hold candidates back (label <c>filters_without_history</c>).</param>
/// <param name="IncludeResearchCells">Off by default. On: a cell in the research state (or with no recorded state) can be a candidate, as the changelog is empty before October 2026 (label <c>research_cells</c>).</param>
public sealed record ReplayParams(DateOnly From, DateOnly To, ReplayScope Scope, decimal Capital, ReplayRiskProfile Profile, string? CostSheetId, ReplayVersionRule VersionRule, ReplayMode Mode, bool FiltersWithoutHistory = false, bool IncludeResearchCells = false)
{
    /// <summary>The problems of the values; empty when they are usable (From not after To, a scope, a positive capital, a usable profile).</summary>
    public IReadOnlyList<string> Problems()
    {
        var p = new List<string>();
        if (From > To)
        {
            p.Add("from must not be after to");
        }

        if (Scope is null || Scope.IsEmpty)
        {
            p.Add("the scope is empty");
        }

        if (Capital <= 0)
        {
            p.Add("capital must be above 0");
        }

        if (Profile is null)
        {
            p.Add("a risk profile is required");
        }
        else
        {
            p.AddRange(Profile.Problems());
        }

        if (!Enum.IsDefined(VersionRule) || !Enum.IsDefined(Mode))
        {
            p.Add("version rule or mode is not known");
        }

        return p;
    }
}
