using System.Globalization;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Platform.Services;

/// <summary>What a save changed, in words, for the audit trail: only field names and values of the settings (none is a secret).</summary>
internal static class ServiceChangeSummary
{
    public static string Describe(string serviceId, ServiceSettings before, ServiceSettings after, bool notBeforeSessionEnd, Func<string, string> nameOf)
    {
        var changes = new List<string>();
        if (before.Enabled != after.Enabled)
        {
            changes.Add($"enabled {YesNo(before.Enabled)} → {YesNo(after.Enabled)}");
        }

        if (before.Schedule != after.Schedule)
        {
            changes.Add($"schedule {ScheduleWords.Describe(before.Schedule, nameOf, notBeforeSessionEnd)} → {ScheduleWords.Describe(after.Schedule, nameOf, notBeforeSessionEnd)}");
        }

        if (before.Retries != after.Retries)
        {
            changes.Add($"retries {Number(before.Retries)} → {Number(after.Retries)}");
        }

        if (before.TimeoutMinutes != after.TimeoutMinutes)
        {
            changes.Add($"timeout {Number(before.TimeoutMinutes)} → {Number(after.TimeoutMinutes)} min");
        }

        if (before.CatchUp != after.CatchUp)
        {
            changes.Add($"catch up {YesNo(before.CatchUp)} → {YesNo(after.CatchUp)}");
        }

        if (before.NotifyOnFailure != after.NotifyOnFailure)
        {
            changes.Add($"notify on failure {YesNo(before.NotifyOnFailure)} → {YesNo(after.NotifyOnFailure)}");
        }

        foreach (var (name, value) in after.Parameters)
        {
            if (!before.Parameters.TryGetValue(name, out var old) || old.GetRawText() != value.GetRawText())
            {
                changes.Add($"parameter {name} {(old.ValueKind == System.Text.Json.JsonValueKind.Undefined ? "(default)" : old.GetRawText())} → {value.GetRawText()}");
            }
        }

        return $"{serviceId}: {(changes.Count == 0 ? "no change" : string.Join("; ", changes))}";
    }

    private static string YesNo(bool value) => value ? "on" : "off";

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
