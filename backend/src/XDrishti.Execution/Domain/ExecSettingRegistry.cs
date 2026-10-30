using System.Globalization;
using XDrishti.Domain.Execution;
using XDrishti.Domain.Execution.Confirm;

namespace XDrishti.Execution.Domain;

public enum ExecSettingKind
{
    Number,
    Flag,
    TimeOfDay,
    Text,
}

/// <summary>Which way a change makes the system riskier (spec P12-07 D-risk, used from P12-11): a higher value, a lower value, or neither.</summary>
public enum ExecRiskDirection
{
    None,
    HigherIsRiskier,
    LowerIsRiskier,
}

/// <summary>One setting: its default and the range it may take. The upper bound is the compiled ceiling; a ceiling changes only by code.</summary>
public sealed record ExecSetting(string Key, ExecSettingKind Kind, string Default, string Min, string Max, ExecRiskDirection Risk = ExecRiskDirection.None);

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
        Num("exec.max_loss_per_trade_inr", "1000", "100", "50000"),
        Num("exec.max_spread_ticks", "3", "1", "20"),
        Num("exec.max_spread_pct", "0.25", "0.05", "1"),
        Num("exec.min_depth_multiple", "2", "1", "10"),
        Num("exec.check_valid_for_s", "10", "2", "60"),
        Num("exec.stop_limit_buffer_pct", "0.5", "0.1", "2"),
        Num("exec.flatten_step_collar_pct", "0.5", "0.1", "2"),
        Num("exec.flatten_walk_steps", "3", "1", "5"),
        Num("exec.breakeven_at_r", "0", "0", "3"),
        Num("exec.stop_unfilled_s", "5", "2", "30"),
        Num("exec.blind_alarm_s", "30", "10", "120"),
        Num("exec.drawdown_halt_pct", "8.0", "1", "30"),
        Num("exec.disaster_loss_pct", "3.0", "0.5", "10"),
        Num("exec.feed_loss_halt_s", "15", "5", "60"),
        Num("exec.runaway_orders_10s", "6", "3", "20"),
        Num("exec.reject_burst", "5", "2", "20"),
        Num("exec.crash_loop_starts", "3", "2", "10"),
        Num("exec.clock_skew_halt_ms", "2000", "500", "5000"),
        Num("exec.index_halt_pct", "9.0", "3", "20"),
        Num("exec.broker_outage_s", "60", "20", "300"),
        new("exec.broker_kill_switch_on_halt", ExecSettingKind.Flag, "false", "false", "true"),
        Num("exec.poll_busy_s", "5", "2", "30"),
        Num("exec.poll_idle_s", "30", "10", "120"),
        Num("exec.recon_interval_s", "60", "30", "300"),
        Num("exec.recon_grace_s", "30", "10", "120"),
        Num("exec.stream_backoff_max_s", "30", "10", "120"),
        Num("exec.recovery_max_s", "60", "20", "300"),
        Time("exec.ready_token_until", "16:45", "16:45", "18:00", ExecRiskDirection.LowerIsRiskier),
        Num("exec.readiness_interval_s", "300", "60", "900", ExecRiskDirection.HigherIsRiskier),
        new("exec.expected_static_ip", ExecSettingKind.Text, string.Empty, string.Empty, string.Empty),
        Num("exec.attestation_max_age_days", "30", "7", "90", ExecRiskDirection.HigherIsRiskier),
        Num(ConfirmSettings.TtlKey, "90", "15", "300", ExecRiskDirection.HigherIsRiskier),
        Num(ConfirmSettings.DriftKey, "0.30", "0.05", "1.00", ExecRiskDirection.HigherIsRiskier),
        Num(ConfirmSettings.StepUpKey, "10", "1", "100", ExecRiskDirection.HigherIsRiskier),
    ];

    public static ExecSetting? Find(string key) => All.FirstOrDefault(s => s.Key == key);

    /// <summary>True when <paramref name="value"/> parses for the setting's kind and lies in its range.</summary>
    public static bool IsValid(ExecSetting setting, string value) => setting.Kind switch
    {
        ExecSettingKind.Number => decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            && v >= Parse(setting.Min) && v <= Parse(setting.Max),
        ExecSettingKind.Flag => bool.TryParse(value, out _),
        ExecSettingKind.Text => value.Length == 0 || IpLiteral.TryNormalize(value, out var ip) && ip == value, // the only text setting is the expected static IP: empty (not set) or one normal-form literal
        _ => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            && t >= TimeOnly.ParseExact(setting.Min, "HH:mm", CultureInfo.InvariantCulture) && t <= TimeOnly.ParseExact(setting.Max, "HH:mm", CultureInfo.InvariantCulture),
    };

    private static decimal Parse(string s) => decimal.Parse(s, CultureInfo.InvariantCulture);

    private static ExecSetting Num(string key, string def, string min, string max, ExecRiskDirection risk = ExecRiskDirection.None) => new(key, ExecSettingKind.Number, def, min, max, risk);

    private static ExecSetting Time(string key, string def, string min, string max, ExecRiskDirection risk = ExecRiskDirection.None) => new(key, ExecSettingKind.TimeOfDay, def, min, max, risk);
}
