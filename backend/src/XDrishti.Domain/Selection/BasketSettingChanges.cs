using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Selection;

/// <summary>
/// The owner acts of the basket settings (spec BK-08, criteria 10 and 11): the only way a row comes to be. An actor that is not <c>owner:...</c> is refused before anything is stored; values are checked; each key that changes is a row with its direction.
/// Direction: a lower cap (or a cap set from OFF) and a lower cut tighten; a higher or cleared cap, a higher cut and ANY change of the breadth source loosen; a cut changed while the cap is OFF before and after alters nothing in effect and is neutral.
/// </summary>
public static class BasketSettingChanges
{
    /// <summary>Stable problem code of a value out of range or a change that changes nothing.</summary>
    public const string InvalidCode = "selection.invalid";

    /// <summary>Stable problem code of a non-owner actor.</summary>
    public const string OwnerOnlyCode = "selection.owner_only";

    /// <summary>The rows for the keys that change. A value left null stays; <paramref name="clearCap"/> turns the cap OFF (and cannot be combined with a cap value). Fails for a non-owner, a value out of range, or when nothing changes.</summary>
    public static Result<IReadOnlyList<BasketSettingChange>> For(string? actor, BasketSettings current, string? breadthSource, double? maxClusterRiskPct, bool clearCap, double? clusterRho)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!OwnerConfirmation.IsOwnerActor(actor) || actor!.Length > 100)
        {
            return Fail(Error.Forbidden(OwnerOnlyCode, "Only the signed-in owner changes the basket settings."));
        }

        if (breadthSource is not null && !BasketSettings.SourceIsValid(breadthSource))
        {
            return Fail(Error.Validation(InvalidCode, "breadthSource: 'tracked' or 'basket:<basket id>'."));
        }

        if (maxClusterRiskPct is { } cap && (!double.IsFinite(cap) || cap <= 0 || cap > 100))
        {
            return Fail(Error.Validation(InvalidCode, "maxClusterRiskPct: above 0 and at most 100 (percent of capital)."));
        }

        if (clearCap && maxClusterRiskPct is not null)
        {
            return Fail(Error.Validation(InvalidCode, "clearCap cannot be combined with maxClusterRiskPct."));
        }

        if (clusterRho is { } rho && (!double.IsFinite(rho) || rho is < 0.1 or > 1.0))
        {
            return Fail(Error.Validation(InvalidCode, "clusterRho: 0.1 to 1.0."));
        }

        var rows = new List<BasketSettingChange>();
        if (breadthSource is { } source && source != current.BreadthSource)
        {
            rows.Add(Row(BasketSettings.BreadthSourceKey, current.BreadthSource, source, BasketSettingChange.Loosen, actor));
        }

        var newCap = clearCap ? null : maxClusterRiskPct ?? current.MaxClusterRiskPct;
        var capChanged = !Same(newCap, current.MaxClusterRiskPct);
        if (capChanged)
        {
            var direction = current.MaxClusterRiskPct is not { } old ? BasketSettingChange.Tighten : newCap is not { } now ? BasketSettingChange.Loosen : now < old ? BasketSettingChange.Tighten : BasketSettingChange.Loosen;
            rows.Add(Row(BasketSettings.MaxClusterRiskKey, BasketSettings.CapText(current.MaxClusterRiskPct), BasketSettings.CapText(newCap), direction, actor));
        }

        if (clusterRho is { } newRho && Math.Abs(newRho - current.ClusterRho) > 1e-12)
        {
            // With the cap OFF before and after the cut is not read by anything: neutral. Otherwise a higher cut makes smaller clusters, so caps bind less.
            var direction = current.MaxClusterRiskPct is null && newCap is null ? BasketSettingChange.Neutral : newRho > current.ClusterRho ? BasketSettingChange.Loosen : BasketSettingChange.Tighten;
            rows.Add(Row(BasketSettings.ClusterRhoKey, BasketSettings.Text(current.ClusterRho), BasketSettings.Text(newRho), direction, actor));
        }

        return rows.Count == 0
            ? Fail(Error.Validation(InvalidCode, "Nothing changes: every value given is the value in force."))
            : Result.Success<IReadOnlyList<BasketSettingChange>>(rows);
    }

    private static bool Same(double? a, double? b) => a is null ? b is null : b is { } y && Math.Abs(a.Value - y) <= 1e-12;

    private static Result<IReadOnlyList<BasketSettingChange>> Fail(Error error) => Result.Failure<IReadOnlyList<BasketSettingChange>>(error);

    private static BasketSettingChange Row(string key, string old, string value, string direction, string actor)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{key}|{old}|{value}|{direction}|{actor}")));
        return new BasketSettingChange(key, old, value, direction, actor, hash);
    }
}
