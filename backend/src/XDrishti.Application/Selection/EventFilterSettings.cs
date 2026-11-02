using System.Text.Json;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Selection;

/// <summary>Shared reading and writing rules of the switch rows (P15-06): the effective state from the newest row, and the JSON snapshot of the settings a row carries.</summary>
internal static class EventFilterSettings
{
    public const int HistorySize = 50;

    /// <summary>The state in force from rows given newest first; no row is OFF.</summary>
    public static EventFilterState StateOf(IReadOnlyList<EventFilterRow> newestFirst) =>
        newestFirst.Count == 0
            ? EventFilterState.Off
            : new EventFilterState(newestFirst[0].Enabled, newestFirst[0].ResultsWithinDays, newestFirst[0].Approval, newestFirst[0].DecidedBy, !newestFirst[0].Enabled && newestFirst[0].DecidedBy == EventFilterState.OwnerDecision && newestFirst.Count > 1 && newestFirst[1].Enabled);

    /// <summary>The JSON of the three settings (and, for an automatic row, the state and rule version of the verdict that decided): the exact settings of the row, nothing else.</summary>
    public static string Snapshot(bool enabled, int days, string approval, EventFilterVerdict? verdict = null)
    {
        var values = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["smart_rules.event_filter.enabled"] = enabled,
            ["smart_rules.event_filter.results_within_days"] = days,
            ["smart_rules.event_filter.approval"] = approval,
        };
        if (verdict is not null)
        {
            values["verdict.state"] = verdict.State;
            values["verdict.rule_version"] = verdict.RuleVersion;
        }

        return JsonSerializer.Serialize(values);
    }

    public static EventFilterRowView View(EventFilterRow row) =>
        new(row.Id, row.Enabled, row.ResultsWithinDays, row.Approval, row.DecidedBy, row.Actor, row.Reason, row.SettingSnapshot, row.VerdictRef, row.ChangedAt);
}
