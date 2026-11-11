using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Replay;

/// <summary>
/// The one snapshot of the settings a replay uses for every session (SR-01, Q2; hindsight label <c>settings_now</c>): the selection, smart-rule and fill limits as the live builder has them, the owner's basket and sizing settings with their versions, the basket risk budgets, and the
/// incomplete-data share. Taken once at the start of the run and recorded with it.
/// </summary>
/// <param name="Rules">The selection rules.</param>
/// <param name="Smart">The smart-rule options.</param>
/// <param name="Fill">The portfolio limits.</param>
/// <param name="Basket">The basket settings (gate breadth source, cluster cap) and their version.</param>
/// <param name="Sizing">The sizing settings (already held at the capacity ceiling) and their version.</param>
/// <param name="Budgets">The basket risk budgets by basket id.</param>
/// <param name="IncompleteDataShare">The share of incomplete bars above which a session is <c>data_missing</c>.</param>
public sealed record ReplaySettingsSnapshot(
    SelectionRules Rules,
    SmartRulesOptions Smart,
    FillConstraints Fill,
    BasketSettingsState Basket,
    SizingState Sizing,
    IReadOnlyDictionary<Guid, double> Budgets,
    double IncompleteDataShare);
