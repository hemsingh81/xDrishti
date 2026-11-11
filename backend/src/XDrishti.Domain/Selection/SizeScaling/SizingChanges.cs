using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Selection.SizeScaling;

/// <summary>
/// The owner acts of the size scaling (spec BK-10, criterion 9): the only way a settings row or a budget row comes to be. An actor that is not <c>owner:...</c> is refused before anything is stored, values are checked, and each change carries its direction:
/// <c>loosen</c> is anything that can only raise a suggested quantity or remove a limit (disabling, raising the volatility floor, lowering the budget minimum fit, raising the capacity percent, raising or removing a budget), <c>tighten</c> the opposite.
/// </summary>
public static class SizingChanges
{
    /// <summary>Stable problem code of a value out of range or a change that changes nothing.</summary>
    public const string InvalidCode = "selection.invalid";

    /// <summary>Stable problem code of a non-owner actor.</summary>
    public const string OwnerOnlyCode = "selection.owner_only";

    /// <summary>The lowest budget, percent of the total open risk limit.</summary>
    public const double LowestBudgetPct = 1;

    /// <summary>The highest budget, percent of the total open risk limit.</summary>
    public const double HighestBudgetPct = 100;

    /// <summary>The key of a basket's budget in the history and the hash.</summary>
    public static string BudgetKey(Guid basketId) => "budget:" + basketId.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>The rows for the keys that change (a value left null stays). Fails for a non-owner, a value out of range, or when nothing changes.</summary>
    public static Result<IReadOnlyList<SizingChange>> ForSettings(string? actor, SizingSettings current, bool? volTargetEnabled, double? floor, bool? capacityEnabled, double? capacityPct, double? minFit = null, double capacityPctCeiling = SizingOptions.HighestCapacityPct)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!OwnerConfirmation.IsOwnerActor(actor) || actor!.Length > 100)
        {
            return Result.Failure<IReadOnlyList<SizingChange>>(Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner changes the sizing settings."));
        }

        if (floor is { } f && !SizingOptions.FloorIsValid(f))
        {
            return Result.Failure<IReadOnlyList<SizingChange>>(Error.Validation(InvalidCode, "floor: 0.25 to 1.0."));
        }

        if (minFit is { } m && !SizingOptions.FloorIsValid(m))
        {
            return Result.Failure<IReadOnlyList<SizingChange>>(Error.Validation(InvalidCode, "minFit: 0.25 to 1.0."));
        }

        if (capacityPct is { } p && !SizingOptions.CapacityPctIsValid(p, capacityPctCeiling))
        {
            return Result.Failure<IReadOnlyList<SizingChange>>(Error.Validation(InvalidCode, "capacityPct: above 0 and at most the configured capacity share (0.005); it can only be tightened."));
        }

        var rows = new List<SizingChange>();
        if (volTargetEnabled is { } v && v != current.VolTargetEnabled)
        {
            rows.Add(Row(SizingSettings.VolTargetEnabledKey, Text(current.VolTargetEnabled), Text(v), v ? SizingChange.Tighten : SizingChange.Loosen, actor));
        }

        if (floor is { } nf && Math.Abs(nf - current.Floor) > 1e-12)
        {
            rows.Add(Row(SizingSettings.FloorKey, Text(current.Floor), Text(nf), nf > current.Floor ? SizingChange.Loosen : SizingChange.Tighten, actor));
        }

        if (minFit is { } nm && Math.Abs(nm - current.MinFit) > 1e-12)
        {
            rows.Add(Row(SizingSettings.MinFitKey, Text(current.MinFit), Text(nm), nm < current.MinFit ? SizingChange.Loosen : SizingChange.Tighten, actor));
        }

        if (capacityEnabled is { } c && c != current.CapacityEnabled)
        {
            rows.Add(Row(SizingSettings.CapacityEnabledKey, Text(current.CapacityEnabled), Text(c), c ? SizingChange.Tighten : SizingChange.Loosen, actor));
        }

        if (capacityPct is { } np && Math.Abs(np - current.CapacityPct) > 1e-12)
        {
            rows.Add(Row(SizingSettings.CapacityPctKey, Text(current.CapacityPct), Text(np), np > current.CapacityPct ? SizingChange.Loosen : SizingChange.Tighten, actor));
        }

        return rows.Count == 0
            ? Result.Failure<IReadOnlyList<SizingChange>>(Error.Validation(InvalidCode, "Nothing changes: every value given is the value in force."))
            : Result.Success<IReadOnlyList<SizingChange>>(rows);
    }

    /// <summary>The row for a budget (<c>null</c> removes it; 1 to 100 sets it, two decimals). Adding or lowering is tighten; raising or removing is loosen; the same value (or none to none) changes nothing.</summary>
    public static Result<SizingChange> ForBudget(string? actor, Guid basketId, double? current, double? budgetPct)
    {
        if (!OwnerConfirmation.IsOwnerActor(actor) || actor!.Length > 100)
        {
            return Result.Failure<SizingChange>(Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner changes a basket risk budget."));
        }

        if (basketId == Guid.Empty || (budgetPct is { } b && (!double.IsFinite(b) || b < LowestBudgetPct || b > HighestBudgetPct)))
        {
            return Result.Failure<SizingChange>(Error.Validation(InvalidCode, "budgetPct: 1 to 100, or null to remove the budget."));
        }

        var next = budgetPct is { } n ? Math.Round(n, 2) : (double?)null;
        var old = current is { } o ? Math.Round(o, 2) : (double?)null;
        if (next == old)
        {
            return Result.Failure<SizingChange>(Error.Validation(InvalidCode, "Nothing changes: the budget given is the budget in force."));
        }

        // next differs from old, so: adding is tighten, removing is loosen, otherwise lower is tighten and raise is loosen.
        var direction = old is not { } a ? SizingChange.Tighten : next is not { } z ? SizingChange.Loosen : z < a ? SizingChange.Tighten : SizingChange.Loosen;
        return Result.Success(Row(BudgetKey(basketId), old is { } ov ? Text(ov) : SizingChange.None, next is { } nv ? Text(nv) : SizingChange.None, direction, actor));
    }

    /// <summary>A boolean as stored.</summary>
    public static string Text(bool value) => value ? "true" : "false";

    /// <summary>A number as stored (invariant, no trailing zeros).</summary>
    public static string Text(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static SizingChange Row(string key, string old, string value, string direction, string actor)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}|{old}|{value}|{direction}|{actor}")));
        return new SizingChange(key, old, value, direction, actor, hash);
    }
}
