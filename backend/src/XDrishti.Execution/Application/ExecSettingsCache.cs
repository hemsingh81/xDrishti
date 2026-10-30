using System.Globalization;
using XDrishti.Domain.Execution.Confirm;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain.Intents;
using XDrishti.Execution.Domain.Readiness;

namespace XDrishti.Execution.Application;

/// <summary>
/// The newest settings view the heartbeat service read, kept for the send path. <see cref="Current"/> is null until a read succeeded and again when the last good read is older than <see cref="MaxAge"/> (a database that cannot be read must not leave
/// old, possibly looser, caps in force silently). The values come through <see cref="Domain.ExecSettingRegistry"/>, so each is inside its range.
/// </summary>
public sealed class ExecSettingsCache(TimeProvider clock) : ISendSettings, IReadinessSettings, IIntentSettings, IConfirmSettings
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(60);

    private readonly Lock _gate = new();
    private SendSettings? _value;
    private ReadinessSettings? _readiness;
    private IntentSettings? _intent;
    private ConfirmSettings? _confirm;
    private DateTimeOffset _readAt;

    public void Set(ExecSettingsView view)
    {
        var settings = From(view);
        var readiness = ReadinessFrom(view);
        var intent = IntentFrom(view);
        var confirm = ConfirmSettings.FromValues(view.Values);
        lock (_gate)
        {
            _value = settings;
            _readiness = readiness;
            _intent = intent;
            _confirm = confirm;
            _readAt = clock.GetUtcNow();
        }
    }

    public SendSettings? Current()
    {
        lock (_gate)
        {
            return _value is not null && clock.GetUtcNow() - _readAt <= MaxAge ? _value : null;
        }
    }

    ReadinessSettings? IReadinessSettings.Current()
    {
        lock (_gate)
        {
            return _readiness is not null && clock.GetUtcNow() - _readAt <= MaxAge ? _readiness : null;
        }
    }

    IntentSettings? IIntentSettings.Current()
    {
        lock (_gate)
        {
            return _intent is not null && clock.GetUtcNow() - _readAt <= MaxAge ? _intent : null;
        }
    }

    ConfirmSettings? IConfirmSettings.Current()
    {
        lock (_gate)
        {
            return _confirm is not null && clock.GetUtcNow() - _readAt <= MaxAge ? _confirm : null;
        }
    }

    public static IntentSettings IntentFrom(ExecSettingsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new(
            TimeOnly.ParseExact(view.Values["exec.entry_window_start"], "HH:mm", CultureInfo.InvariantCulture),
            TimeOnly.ParseExact(view.Values["exec.entry_window_end"], "HH:mm", CultureInfo.InvariantCulture),
            decimal.Parse(view.Values["exec.price_band_pct"], NumberStyles.Number, CultureInfo.InvariantCulture),
            bool.Parse(view.Values["exec.canary_pending"]),
            TimeSpan.FromSeconds(Int(view, "exec.feed_max_age_s")));
    }

    public static ReadinessSettings ReadinessFrom(ExecSettingsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        return new(
            TimeOnly.ParseExact(view.Values["exec.ready_token_until"], "HH:mm", CultureInfo.InvariantCulture),
            Int(view, "exec.token_min_remaining_min"),
            view.Values["exec.expected_static_ip"],
            Int(view, "exec.attestation_max_age_days"),
            Int(view, "exec.readiness_interval_s"));
    }

    public static SendSettings From(ExecSettingsView view) => new(
        PerSecond: Int(view, "exec.max_orders_per_second"),
        PerMinute: Int(view, "exec.max_orders_per_minute"),
        PerDay: Int(view, "exec.max_orders_per_day"),
        OrderValidFor: TimeSpan.FromSeconds(Int(view, "exec.order_valid_for_s")),
        SendTimeout: TimeSpan.FromSeconds(Int(view, "exec.send_timeout_s")),
        ExitCollarPct: decimal.Parse(view.Values["exec.exit_collar_pct"], NumberStyles.Number, CultureInfo.InvariantCulture),
        TokenMinRemainingMinutes: Int(view, "exec.token_min_remaining_min"));

    private static int Int(ExecSettingsView view, string key) => (int)decimal.Parse(view.Values[key], NumberStyles.Number, CultureInfo.InvariantCulture);
}
