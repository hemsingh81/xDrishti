namespace XDrishti.Domain.Selection;

/// <summary>What <c>max_new_entries_per_day</c> limits (doc 10 section 5, <c>count_entries_on</c>).</summary>
public enum EntriesCountMode
{
    /// <summary>The plan lists up to <c>MaxPlanItems</c> conditional tickets and the entries limit is enforced when they trigger. The other limits are checked as if every listed ticket triggers (safe side).</summary>
    Trigger,

    /// <summary>The plan itself holds at most <c>MaxEntriesPerDay</c> tickets.</summary>
    Plan,
}
