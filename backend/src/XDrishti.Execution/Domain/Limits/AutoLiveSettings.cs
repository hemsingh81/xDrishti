using System.Globalization;

namespace XDrishti.Execution.Domain.Limits;

/// <summary>
/// The newest settings row as the <c>auto_live</c> path reads it (spec P12-09 Assumption 1, criterion 12): the two switches (<c>auto_live_enabled</c> and <c>execution_enabled</c>, both OFF without a row), the row id the ledger and the
/// action log carry, the four <c>exec.auto_live_*</c> keys and P12-01's global caps, which the sheet can only narrow.
/// </summary>
public sealed record AutoLiveSettings(
    long? SettingsId,
    bool ExecutionEnabled,
    bool AutoLiveEnabled,
    bool AllowAutoApproved,
    bool RequireDayUnlock,
    bool RequireAttended,
    int AttendedMinutes,
    decimal GlobalMaxOrderValue,
    int GlobalMaxOrdersPerDay,
    TimeOnly GlobalWindowStart,
    TimeOnly GlobalWindowEnd)
{
    public const string AllowAutoApprovedKey = "exec.auto_live_allow_auto_approved";
    public const string RequireDayUnlockKey = "exec.auto_live_require_day_unlock";
    public const string RequireAttendedKey = "exec.auto_live_require_attended";
    public const string AttendedMinutesKey = "exec.auto_live_attended_min";

    /// <summary>No settings row: both switches OFF and every key at its registry default.</summary>
    public static AutoLiveSettings None { get; } = From(null, false, false, null);

    /// <summary>The settings of a row; without a row id both switches read OFF, and auto_live is never ON without execution. A key that is absent, unparsable or out of range takes the registry default (the same rule as <c>ExecSettingsReader</c>).</summary>
    public static AutoLiveSettings From(long? settingsId, bool executionEnabled, bool autoLiveEnabled, IReadOnlyDictionary<string, string>? values) => new(
        settingsId,
        executionEnabled && settingsId is not null,
        autoLiveEnabled && executionEnabled && settingsId is not null,
        bool.Parse(Value(values, AllowAutoApprovedKey)),
        bool.Parse(Value(values, RequireDayUnlockKey)),
        bool.Parse(Value(values, RequireAttendedKey)),
        (int)decimal.Parse(Value(values, AttendedMinutesKey), CultureInfo.InvariantCulture),
        decimal.Parse(Value(values, "exec.max_order_value_inr"), CultureInfo.InvariantCulture),
        (int)decimal.Parse(Value(values, "exec.max_orders_per_day"), CultureInfo.InvariantCulture),
        TimeOnly.ParseExact(Value(values, "exec.entry_window_start"), "HH:mm", CultureInfo.InvariantCulture),
        TimeOnly.ParseExact(Value(values, "exec.entry_window_end"), "HH:mm", CultureInfo.InvariantCulture));

    private static string Value(IReadOnlyDictionary<string, string>? values, string key)
    {
        var setting = ExecSettingRegistry.Find(key) ?? throw new InvalidOperationException("Unknown setting " + key);
        return values is not null && values.TryGetValue(key, out var text) && ExecSettingRegistry.IsValid(setting, text) ? text : setting.Default;
    }
}
