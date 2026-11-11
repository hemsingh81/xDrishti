using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Auth;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Selection;

/// <summary>One change of a basket setting in the history.</summary>
public sealed record BasketSettingChangeView(long Id, string Key, string OldValue, string NewValue, string Direction, string Actor, DateTimeOffset ChangedAt, string Hash);

/// <summary>
/// The basket settings in force with their history (newest first, the latest 50). With no row everything is OFF: the gate reads the tracked set's breadth and no cap on open risk per correlation cluster applies.
/// </summary>
/// <param name="BreadthSource"><c>gate.breadth_source</c>: <c>tracked</c> or <c>basket:&lt;id&gt;</c>.</param>
/// <param name="MaxClusterRiskPct"><c>selection.max_cluster_risk_pct</c>, percent of capital; null is OFF.</param>
/// <param name="ClusterRho"><c>selection.cluster_rho</c>.</param>
/// <param name="Version">The newest row id (0 when none).</param>
/// <param name="Notice">Advisory, no order.</param>
/// <param name="LatestLoosening">The newest change with direction loosen, or null when none was ever made.</param>
/// <param name="History">The changes, newest first.</param>
public sealed record BasketSettingsView(string BreadthSource, double? MaxClusterRiskPct, double ClusterRho, long Version, string Notice, BasketSettingChangeView? LatestLoosening, IReadOnlyList<BasketSettingChangeView> History);

/// <summary>Reads the basket settings and their history.</summary>
public sealed record GetBasketSettingsQuery : IQuery<BasketSettingsView>;

/// <summary>
/// The owner's change of the basket settings (BK-08). The endpoint checked the session and the CSRF token and built the actor from the session; the fresh password is checked here for EVERY change, without opening the elevation window. A value left null stays;
/// <paramref name="ClearCap"/> turns the cluster cap OFF.
/// </summary>
public sealed record ChangeBasketSettingsCommand(string Actor, Guid SessionId, ClientInfo Client, string? BreadthSource, double? MaxClusterRiskPct, bool ClearCap, double? ClusterRho, string? Password) : ICommand;

/// <summary>Builds the view of the stored settings.</summary>
internal static class BasketSettingsViews
{
    public const int HistoryLimit = 50;

    public const string Notice = "The cluster cap and the gate's breadth source only ever make a plan stricter or change what the gate reads; both are OFF until the owner sets them, and every change needs the owner's password. Advisory: no order is placed in any setting.";

    // shortcut: the "gate source changed" plan banner until dismissed (BK-08 AC 11) is moved to BK-10b with the sizing banner; the view reports the newest loosening. Upgrade when the dismissal state and the screen exist.
    public static async ValueTask<BasketSettingsView> ReadAsync(IBasketSettingsStore store, CancellationToken cancellationToken)
    {
        var state = await store.CurrentAsync(cancellationToken);
        var history = (await store.HistoryAsync(HistoryLimit, cancellationToken)).Select(r => new BasketSettingChangeView(r.Id, r.Key, r.OldValue, r.NewValue, r.Direction, r.Actor, r.ChangedAt, r.Hash)).ToList();
        var s = state.Settings;
        return new BasketSettingsView(s.BreadthSource, s.MaxClusterRiskPct, s.ClusterRho, state.Version, Notice, history.FirstOrDefault(h => h.Direction == BasketSettingChange.Loosen), history);
    }
}
