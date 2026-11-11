namespace XDrishti.Domain.Planning;

/// <summary>
/// The pure rules of which loosenings a plan shows and which an automatic dismissal may hide (BK-10b). A loosening shows from the first plan built after it until the owner dismisses it, or, with the automatic setting ON, until one plan has shown it.
/// Nothing here reads a clock or a store, and nothing here changes a limit.
/// </summary>
public static class LooseningNotices
{
    /// <summary>The actor of an automatic dismissal.</summary>
    public const string AutoActor = "auto";

    /// <summary>
    /// The loosenings a plan shows, oldest first: of the undismissed ones (a dismissal is of the change, so the store has already left it out for every plan read after it), those changed at or before the plan was built.
    /// </summary>
    public static IReadOnlyList<LooseningNotice> Shown(IEnumerable<LooseningNotice> undismissed, DateTimeOffset planCreatedAt)
    {
        ArgumentNullException.ThrowIfNull(undismissed);
        return [.. undismissed.Where(l => l.ChangedAt <= planCreatedAt).OrderBy(l => l.ChangedAt).ThenBy(l => l.Source, StringComparer.Ordinal).ThenBy(l => l.Id)];
    }

    /// <summary>
    /// The loosenings an automatic dismissal may hide at the build of a plan: only with the setting ON, and only those a published plan already showed, that is changed at or before the previous plan was built.
    /// A loosening no plan has shown is never hidden; with no earlier plan nothing is.
    /// </summary>
    public static IReadOnlyList<LooseningNotice> AutoDismissible(IEnumerable<LooseningNotice> undismissed, bool autoDismissOn, DateTimeOffset? previousPlanCreatedAt)
    {
        ArgumentNullException.ThrowIfNull(undismissed);
        return !autoDismissOn || previousPlanCreatedAt is not { } shownBy ? [] : [.. undismissed.Where(l => l.ChangedAt <= shownBy)];
    }
}
