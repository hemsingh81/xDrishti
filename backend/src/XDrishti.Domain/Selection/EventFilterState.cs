using XDrishti.Domain.Filings;

namespace XDrishti.Domain.Selection;

/// <summary>
/// The three settings <c>smart_rules.event_filter.enabled|results_within_days|approval</c> as the newest row of the decision history says (P15-06, ADR 0182). <see cref="Off"/> is the state of no row and of a table that cannot be read: the filter is not acting.
/// </summary>
/// <param name="Enabled">The selection excludes a candidate with results within N sessions, and the plan check gets its days.</param>
/// <param name="ResultsWithinDays">N, 0 to 10; 2 by default.</param>
/// <param name="Approval"><see cref="ManualApproval"/> (default) or <see cref="AutoApproval"/>.</param>
/// <param name="OwnerSwitchedOff">The newest row is an owner row that is OFF directly after an ON row: the owner turned the filter off, and the auto option may not turn it on again until the owner turns it on or sets approval manual (ADR 0182).</param>
/// <param name="DecidedBy"><see cref="OwnerDecision"/> or <see cref="AutoDecision"/> for the row in force; null when there is no row.</param>
public sealed record EventFilterState(bool Enabled, int ResultsWithinDays, string Approval, string? DecidedBy, bool OwnerSwitchedOff = false)
{
    public const string ManualApproval = "manual";

    public const string AutoApproval = "auto";

    public const string OwnerDecision = "owner";

    public const string AutoDecision = "auto";

    /// <summary>The actor of an automatic row.</summary>
    public const string AutoActor = "auto:event-filter";

    /// <summary>No row (or an unreadable table): OFF, N = 2, manual.</summary>
    public static EventFilterState Off { get; } = new(false, ResultsWindow.DefaultDays, ManualApproval, null);

    public static bool IsApproval(string? value) => value is ManualApproval or AutoApproval;
}
