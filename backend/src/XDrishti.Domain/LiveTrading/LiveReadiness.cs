using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.LiveTrading;

/// <summary>Whether a background service is there to rely on. A service that is not registered or is paused is not a pass.</summary>
public enum LiveServiceState
{
    NotRegistered,
    Paused,
    Enabled,
}

/// <summary>What the switch of an account to <c>manual_live</c> is judged on (spec P11-04, criterion 3). A null soft fact is "could not be read" and never a pass.</summary>
/// <param name="RoleOn">The Trading role is on.</param>
/// <param name="SettingsComplete">The trading settings are complete (profile, capital, styles, instruments).</param>
/// <param name="CapitalFixed">The capital is a fixed amount (not the broker's funds).</param>
/// <param name="Connection">The Dhan connection now.</param>
/// <param name="StepSet">The account has a ramp step.</param>
/// <param name="Capture">The <c>live-capture</c> service.</param>
/// <param name="Settle">The <c>live-settle</c> service.</param>
/// <param name="PaperPhaseMet">The P10 paper phase check says MET; null when it could not be read.</param>
/// <param name="ChargeSheetConfirmed">The owner confirmed the charge sheet; null when it could not be read.</param>
/// <param name="OrderBookCaptureOn">The order book is captured (nothing reads it before P11-01 and the owner's switch).</param>
public sealed record LiveReadinessFacts(
    bool RoleOn,
    bool SettingsComplete,
    bool CapitalFixed,
    ConnectionStatus Connection,
    bool StepSet,
    LiveServiceState Capture,
    LiveServiceState Settle,
    bool? PaperPhaseMet,
    bool? ChargeSheetConfirmed,
    bool OrderBookCaptureOn);

/// <summary>One line of the readiness: a hard item refuses the switch, a soft item needs the owner's typed reason.</summary>
public sealed record ReadinessItem(string Code, bool Hard, bool Passed, string Detail);

/// <summary>The readiness of an account for <c>manual_live</c>.</summary>
public sealed record LiveReadinessResult(IReadOnlyList<ReadinessItem> Items)
{
    public IReadOnlyList<ReadinessItem> FailingHard => [.. Items.Where(i => i.Hard && !i.Passed)];

    public IReadOnlyList<ReadinessItem> FailingSoft => [.. Items.Where(i => !i.Hard && !i.Passed)];
}

/// <summary>
/// The live-ready gate of an account's mode change (spec P11-04 criterion 3, ADR 0148), pure. Hard items: the Trading role complete with a fixed capital, the Dhan connection <c>connected</c>, a ramp step, and the <c>live-capture</c> and
/// <c>live-settle</c> services registered and enabled. Soft items (the P10 phase check not MET, the charge sheet not confirmed, the order book not captured) pass only with a typed reason. Switching back to paper is not judged here.
/// </summary>
public static class LiveReadiness
{
    public const string TradingRole = "trading_role";
    public const string FixedCapital = "fixed_capital";
    public const string BrokerConnection = "broker_connection";
    public const string RampStep = "ramp_step";
    public const string LiveCapture = "live_capture";
    public const string LiveSettle = "live_settle";
    public const string PaperPhase = "paper_phase_met";
    public const string ChargeSheet = "charge_sheet_confirmed";
    public const string OrderBook = "order_book_capture";

    public static LiveReadinessResult Evaluate(LiveReadinessFacts f)
    {
        ArgumentNullException.ThrowIfNull(f);
        return new LiveReadinessResult(
        [
            new(TradingRole, true, f.RoleOn && f.SettingsComplete, f.RoleOn ? (f.SettingsComplete ? "the Trading role is on and complete" : "the trading settings are not complete") : "the Trading role is off"),
            new(FixedCapital, true, f.CapitalFixed, f.CapitalFixed ? "the capital is a fixed amount" : "the capital must be a fixed amount, not the broker's funds"),
            new(BrokerConnection, true, f.Connection == ConnectionStatus.Connected, $"the Dhan connection is {f.Connection.ToString().ToLowerInvariant()}"),
            new(RampStep, true, f.StepSet, f.StepSet ? "a ramp step is set" : "no ramp step is set: the owner sets S1 first"),
            Service(LiveCapture, "live-capture", f.Capture),
            Service(LiveSettle, "live-settle", f.Settle),
            new(PaperPhase, false, f.PaperPhaseMet == true, f.PaperPhaseMet switch { true => "the paper phase check is MET", false => "the paper phase check is not MET", _ => "the paper phase check could not be read" }),
            new(ChargeSheet, false, f.ChargeSheetConfirmed == true, f.ChargeSheetConfirmed switch { true => "the charge sheet is confirmed", false => "the charge sheet is not confirmed", _ => "the charge sheet could not be read" }),
            new(OrderBook, false, f.OrderBookCaptureOn, f.OrderBookCaptureOn ? "the order book is captured" : "the order book is not captured, so every order-dependent figure is unavailable"),
        ]);
    }

    private static ReadinessItem Service(string code, string id, LiveServiceState state) =>
        new(code, true, state == LiveServiceState.Enabled, state switch
        {
            LiveServiceState.Enabled => $"service {id} is enabled",
            LiveServiceState.Paused => $"service {id} is paused",
            _ => $"service {id} is not registered",
        });
}
