using System.Globalization;
using XDrishti.Application.Portfolio.Attention;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Execution;

/// <summary>Everything the derivation reads, already fetched. The rows are insert-only, so the same input always gives the same items.</summary>
public sealed record ExecutionNoticeInput(
    DateTimeOffset Now,
    bool TradingDay,
    NoticeRaw Raw,
    IReadOnlyList<ExecutionAlarmView> Alarms,
    IReadOnlyList<ExecutionHaltView> Halts,
    IReadOnlyList<ExecutionBreakView> Breaks,
    IReadOnlyList<ExecutionModeAccountView> Modes,
    IReadOnlyList<OverviewAccount> Accounts,
    IReadOnlyList<ExecutionReadinessAccount> Readiness);

/// <summary>
/// The notices of spec P12-10 criterion 10. Every id includes its cause (<c>exec:ticket:{id}</c>, <c>exec:halt:{id}</c>, <c>exec:action:{logId}</c>, <c>exec:day:{date}:{kind}</c>), so a changed situation is a new item. An item whose cause has a successor (an
/// outcome, a release, a clear, a resolution) is kept 24 hours as resolved and is not counted. Pure.
/// </summary>
public static class ExecutionNoticeDeriver
{
    public static readonly TimeSpan Keep = TimeSpan.FromHours(24);

    /// <summary>The share of a cap at which <c>exec_cap_near</c> warns.</summary>
    public const decimal NearShare = 0.8m;

    private static readonly string[] MissingCodes = ["protection_breach", "protection_unknown"];
    private static readonly string[] Automating = ["assisted", "semi_auto", "auto_live"];

    public static IReadOnlyList<ExecutionNotice> Derive(ExecutionNoticeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = input.Now;
        var ist = IndiaTime.ToIst(now);
        var today = IndiaTime.Today(now);
        var cutoff = now - Keep;
        var names = input.Modes.ToDictionary(m => m.AccountId, m => m.Name);
        string Name(Guid? id) => id is { } g && names.TryGetValue(g, out var n) ? n : "the account";
        var items = new List<ExecutionNotice>();

        foreach (var t in input.Raw.Tickets)
        {
            var open = t.Outcome is null && t.ExpiresAt > now;
            items.Add(new ExecutionNotice(
                "exec:ticket:" + t.Id.ToString("N"), "exec_confirm_waiting", "warning", t.Text.Trim() + " Expires " + Hms(t.ExpiresAt) + " IST.", "Order waiting for your confirmation", t.IssuedAt, [Open("today")], t.IssuedAt, !open, t.AccountId));
        }

        foreach (var a in input.Raw.AutoActions)
        {
            items.Add(new ExecutionNotice("exec:action:" + a.Id.ToString(CultureInfo.InvariantCulture), "exec_auto_action", "info", a.Text, "Automatic action", a.At, [Open("activity")], a.At, true, a.AccountId));
        }

        foreach (var r in input.Raw.Rejected)
        {
            items.Add(new ExecutionNotice("exec:intent:" + r.IntentId.ToString("N") + ":rejected", "exec_order_rejected", "error", "The order for " + r.Symbol + " was rejected.", "Order rejected", r.At, [Open("orders")], r.At, r.SessionDate < today, r.AccountId));
        }

        foreach (var a in input.Alarms.Where(a => MissingCodes.Contains(a.Code) && (a.ClearedAt is null || a.ClearedAt >= cutoff)))
        {
            items.Add(new ExecutionNotice(
                "exec:alarm:" + a.Id.ToString(CultureInfo.InvariantCulture), "exec_protection_missing", "error", "A position may have no stop at the broker (" + a.Code + "): " + a.Detail, "Protection missing", a.At, [Open("positions")], a.At, a.ClearedAt is not null, a.AccountId));
        }

        foreach (var h in input.Halts.Where(h => h.End is null || h.End.At >= cutoff))
        {
            var kill = string.Equals(h.Source, "owner", StringComparison.Ordinal);
            items.Add(new ExecutionNotice(
                "exec:halt:" + h.Id.ToString(CultureInfo.InvariantCulture), kill ? "exec_kill_switch" : "exec_halted", "error",
                (kill ? "Trading was stopped by you" : "Trading was halted") + " (level " + h.Level + ", " + h.Trigger + "): " + h.Reason, kill ? "Kill switch" : "Trading halted", h.At, [Open("today")], h.At, h.End is not null, h.AccountId));
        }

        foreach (var b in input.Breaks.Where(b => b.Resolution is null || b.Resolution.At >= cutoff))
        {
            items.Add(new ExecutionNotice(
                "exec:break:" + b.Id.ToString(CultureInfo.InvariantCulture), "exec_recon_break", "error", "Our records and the broker's differ (" + b.Kind + (b.Symbol is null ? string.Empty : ", " + b.Symbol) + "): " + b.Detail, "Reconciliation break", b.At, [Open("activity")], b.At, b.Resolution is not null, b.AccountId));
        }

        foreach (var (mode, trail) in input.Modes.SelectMany(m => m.Trail.Select(t => (m, t))).Where(x => x.t.Kind == "demotion" && x.t.ChangedAt >= cutoff))
        {
            items.Add(new ExecutionNotice(
                "exec:mode:" + trail.Id.ToString(CultureInfo.InvariantCulture), "exec_demoted", "error", Name(mode.AccountId) + " was moved down from " + trail.From + " to " + trail.To + ": " + trail.Reason, "Account demoted", trail.ChangedAt, [Open("modes")], trail.ChangedAt, false, mode.AccountId));
        }

        if (input.TradingDay)
        {
            var readyById = input.Readiness.ToDictionary(r => r.AccountId);
            var severity = ist.Hour * 60 + ist.Minute >= 9 * 60 + 5 ? "error" : "warning";
            var live = ist.Hour * 60 + ist.Minute < 15 * 60 + 30;
            foreach (var m in input.Modes.Where(m => Automating.Contains(m.EffectiveMode)))
            {
                var key = m.AccountId.ToString("N")[..8];
                readyById.TryGetValue(m.AccountId, out var ready);
                if (live && ready?.State != "ready")
                {
                    items.Add(new ExecutionNotice(
                        "exec:day:" + Date(today) + ":not_ready:" + key, "exec_not_ready", severity, Name(m.AccountId) + " is not ready to send today (" + (ready?.State ?? "unavailable") + ").", "Execution not ready", Start(today), [Open("today")], Start(today), false, m.AccountId));
                }

                if (live && ready is not null && ready.Checks.Any(c => c.Outcome == "fail" && c.Code is "ip_rejected" or "ip_configured" or "ip_attested"))
                {
                    items.Add(new ExecutionNotice(
                        "exec:day:" + Date(today) + ":ip_invalid:" + key, "exec_ip_invalid", "error", "The static IP is not valid for " + Name(m.AccountId) + ".", "IP not valid", Start(today), [Open("today")], Start(today), false, m.AccountId));
                }
            }

            foreach (var a in input.Accounts)
            {
                AddNear(items, today, a, "orders", a.Caps.Orders);
                AddNear(items, today, a, "notional", a.Caps.Notional);
                AddNear(items, today, a, "loss", a.Caps.WorstCaseLoss);
            }

            var orders = input.Accounts.Sum(a => a.Caps.Orders.State == "value" ? a.Caps.Orders.Value ?? 0m : 0m);
            if (ist.Hour * 60 + ist.Minute >= 15 * 60 + 45 && orders > 0m)
            {
                items.Add(new ExecutionNotice(
                    "exec:day:" + Date(today) + ":summary", "exec_day_summary", "info", orders.ToString("0", CultureInfo.InvariantCulture) + " order(s) were sent today across " + input.Accounts.Count(a => a.Caps.Orders.Value > 0m).ToString(CultureInfo.InvariantCulture) + " account(s).", "Day summary", Start(today).AddHours(15).AddMinutes(45), [Open("activity")], Start(today).AddHours(15).AddMinutes(45), true, null));
            }
        }

        return [.. items.OrderBy(i => i.Resolved).ThenByDescending(i => i.At).Take(100)];
    }

    private static void AddNear(List<ExecutionNotice> items, DateOnly today, OverviewAccount account, string metric, ExecMetric m)
    {
        if (m.State != "value" || m.Value is not { } used || m.Cap is not { } cap || cap <= 0m || used < cap * NearShare)
        {
            return;
        }

        var what = metric switch { "orders" => "orders", "notional" => "notional", _ => "worst-case loss" };
        items.Add(new ExecutionNotice(
            "exec:day:" + Date(today) + ":cap_near:" + metric + ":" + account.AccountId.ToString("N")[..8], "exec_cap_near", "warning",
            account.Name + " has used " + used.ToString("0.##", CultureInfo.InvariantCulture) + " of " + cap.ToString("0.##", CultureInfo.InvariantCulture) + " " + what + " for the day.", "Near a limit", Start(today).AddHours(9), [Open("today")], Start(today).AddHours(9), false, account.AccountId));
    }

    private static AttentionAction Open(string tab) => new("Open", "route", "/execution?tab=" + tab);

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset Start(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);

    private static string Hms(DateTimeOffset at) => IndiaTime.ToIst(at).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
}
