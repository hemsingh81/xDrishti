using System.Globalization;

namespace XDrishti.Execution.Domain;

public enum ExecSettingKind
{
    Number,
    Flag,
    TimeOfDay,
}

/// <summary>One setting: its default and the range it may take. The upper bound is the compiled ceiling; a ceiling changes only by code.</summary>
public sealed record ExecSetting(string Key, ExecSettingKind Kind, string Default, string Min, string Max);

/// <summary>The registry of execution settings (P12-01) and the rule that decides a value. It moves to the shared Domain when P12-07 gives xd-api a use for it.</summary>
public static class ExecSettingRegistry
{
    public static readonly IReadOnlyList<ExecSetting> All =
    [
        Num("exec.max_orders_per_second", "2", "1", "5"),
        Num("exec.max_orders_per_minute", "30", "1", "120"),
        Num("exec.max_orders_per_day", "200", "10", "1000"),
        Num("exec.max_order_value_inr", "100000", "1000", "1000000"),
        Num("exec.max_order_qty", "2000", "1", "10000"),
        Num("exec.price_band_pct", "2.0", "0.1", "5"),
        Num("exec.feed_max_age_s", "5", "1", "30"),
        Num("exec.protect_deadline_s", "10", "3", "60"),
        new("exec.flatten_on_unprotected", ExecSettingKind.Flag, "true", "false", "true"),
        Time("exec.entry_window_start", "09:20", "00:00", "23:59"),
        Time("exec.entry_window_end", "15:00", "00:00", "23:59"),
        Time("exec.square_off_time", "15:15", "15:00", "15:20"),
        Num("exec.token_min_remaining_min", "20", "5", "120"),
        Num("exec.max_modifications_per_intent", "20", "0", "24"),
        Num("exec.exit_collar_pct", "1.0", "0.2", "3"),
        Num("exec.send_timeout_s", "10", "3", "30"),
        Num("exec.order_valid_for_s", "5", "1", "30"),
        new("exec.lookup_verified", ExecSettingKind.Flag, "false", "false", "true"),
        new("exec.canary_pending", ExecSettingKind.Flag, "true", "false", "true"),
    ];

    public static ExecSetting? Find(string key) => All.FirstOrDefault(s => s.Key == key);

    /// <summary>True when <paramref name="value"/> parses for the setting's kind and lies in its range.</summary>
    public static bool IsValid(ExecSetting setting, string value) => setting.Kind switch
    {
        ExecSettingKind.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            && v >= Parse(setting.Min) && v <= Parse(setting.Max),
        ExecSettingKind.Flag => bool.TryParse(value, out _),
        _ => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            && t >= TimeOnly.ParseExact(setting.Min, "HH:mm", CultureInfo.InvariantCulture) && t <= TimeOnly.ParseExact(setting.Max, "HH:mm", CultureInfo.InvariantCulture),
    };

    private static decimal Parse(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    private static ExecSetting Num(string key, string def, string min, string max) => new(key, ExecSettingKind.Number, def, min, max);

    private static ExecSetting Time(string key, string def, string min, string max) => new(key, ExecSettingKind.TimeOfDay, def, min, max);
}
