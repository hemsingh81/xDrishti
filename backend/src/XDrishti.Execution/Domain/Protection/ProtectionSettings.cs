using System.Globalization;

namespace XDrishti.Execution.Domain.Protection;

/// <summary>
/// The numbers the protection logic uses (spec P12-04). Read from the settings in force; <see cref="FromValues"/> returns null when any value is out of range, which the caller treats as "cannot vouch" (no flatten, an alarm and a halt request),
/// never as a default that might be looser.
/// </summary>
public sealed record ProtectionSettings(
    TimeSpan Deadline,
    decimal StopLimitBufferPct,
    bool FlattenOnUnprotected,
    decimal FlattenStepCollarPct,
    int FlattenWalkSteps,
    decimal BreakevenAtR,
    TimeSpan StopUnfilled,
    TimeSpan BlindAfter,
    decimal ExitCollarPct,
    int MaxModifications,
    TimeOnly EntryWindowEnd,
    TimeOnly SquareOffTime,
    bool LookupVerified)
{
    public static ProtectionSettings Default { get; } = FromValues(new Dictionary<string, string>())!;

    public static ProtectionSettings? FromValues(IReadOnlyDictionary<string, string> values)
    {
        if (Num(values, "exec.protect_deadline_s") is not { } deadline
            || Num(values, "exec.stop_limit_buffer_pct") is not { } buffer
            || Raw(values, "exec.flatten_on_unprotected") is not { } flatten
            || Num(values, "exec.flatten_step_collar_pct") is not { } step
            || Num(values, "exec.flatten_walk_steps") is not { } steps
            || Num(values, "exec.breakeven_at_r") is not { } breakeven
            || Num(values, "exec.stop_unfilled_s") is not { } unfilled
            || Num(values, "exec.blind_alarm_s") is not { } blind
            || Num(values, "exec.exit_collar_pct") is not { } collar
            || Num(values, "exec.max_modifications_per_intent") is not { } mods
            || Time(values, "exec.entry_window_end") is not { } entryEnd
            || Time(values, "exec.square_off_time") is not { } squareOff
            || Raw(values, "exec.lookup_verified") is not { } verified)
        {
            return null;
        }

        return new ProtectionSettings(TimeSpan.FromSeconds((double)deadline), buffer, bool.Parse(flatten), step, (int)steps, breakeven, TimeSpan.FromSeconds((double)unfilled), TimeSpan.FromSeconds((double)blind), collar, (int)mods, entryEnd, squareOff, bool.Parse(verified));
    }

    private static string? Raw(IReadOnlyDictionary<string, string> values, string key)
    {
        var setting = ExecSettingRegistry.Find(key);
        if (setting is null)
        {
            return null;
        }

        var raw = values.TryGetValue(key, out var v) ? v : setting.Default;
        return ExecSettingRegistry.IsValid(setting, raw) ? raw : null;
    }

    private static decimal? Num(IReadOnlyDictionary<string, string> values, string key) => Raw(values, key) is { } raw ? decimal.Parse(raw, CultureInfo.InvariantCulture) : null;

    private static TimeOnly? Time(IReadOnlyDictionary<string, string> values, string key) => Raw(values, key) is { } raw ? TimeOnly.ParseExact(raw, "HH:mm", CultureInfo.InvariantCulture) : null;
}
