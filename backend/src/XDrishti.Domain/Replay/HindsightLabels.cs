namespace XDrishti.Domain.Replay;

/// <summary>The hindsight labels of a replay (SR-01, AC 10): which parts of the result a trader could not have had at the time. Stated on every run and every response, never hidden.</summary>
public static class HindsightLabels
{
    /// <summary>The strategies were written after the start date and are run backwards (version rule start).</summary>
    public const string VersionsStart = "versions_start";

    /// <summary>The settings are today's, not those of the session.</summary>
    public const string SettingsNow = "settings_now";

    /// <summary>Membership before a basket's known-since date is the earliest known membership (ISS-22).</summary>
    public const string Survivorship = "survivorship";

    /// <summary>The strategies were designed on this history.</summary>
    public const string DataSeenBefore = "data_seen_before";

    /// <summary>No filled-in charge sheet: the figures are gross (ISS-25).</summary>
    public const string BeforeCosts = "before_costs";

    /// <summary>Learning mode used the cell lifecycle as recorded, not recomputed (Q4).</summary>
    public const string LifecycleAsRecorded = "lifecycle_as_recorded";

    /// <summary>The labels that always apply, plus those the parameters add; the survivorship label is added per session by the facts.</summary>
    public static IReadOnlyList<string> For(ReplayParams p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var labels = new List<string>();
        if (p.VersionRule == ReplayVersionRule.Start)
        {
            labels.Add(VersionsStart);
        }

        labels.Add(SettingsNow);
        labels.Add(DataSeenBefore);
        if (p.CostSheetId is null)
        {
            labels.Add(BeforeCosts);
        }

        if (p.Mode == ReplayMode.Learning)
        {
            labels.Add(LifecycleAsRecorded);
        }

        return labels;
    }
}
