using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using XDrishti.Domain.Paper;

namespace XDrishti.Domain.Shadow;

/// <summary>
/// The rules of the frozen evidence set of an edge epoch (spec AM-01 GAP-09, ADR 0282): the strategy versions whose shadow rows count for edge and for the readiness forward rows. By default the versions of the eligible cells of the newest full configuration snapshot,
/// fixed by this rule before any forward result of the epoch exists; the owner may replace the set until the epoch's first shadow write. Pure: no clock and no I/O.
/// </summary>
public static partial class EvidenceSetRules
{
    /// <summary>The trial kind of a registered version in <c>lab.test_ledger</c>. A registration is not a look: the variant counts leave these rows out.</summary>
    public const string TrialKind = "evidence_set";

    /// <summary>The set was registered by the rule at the epoch's first shadow write.</summary>
    public const string ModeAuto = "auto";

    /// <summary>The set was named by the owner before the epoch's first shadow write.</summary>
    public const string ModeOwner = "owner";

    /// <summary>The actor of an automatic set.</summary>
    public const string AutoActor = "system:default";

    /// <summary>The most versions a set may hold (a guard, not a design limit).</summary>
    public const int MaxVersions = 500;

    /// <summary>The key a set is stored under: the paper epoch's id, or 0 while no paper epoch exists (the rows written then carry no epoch).</summary>
    public static long EpochKey(long? edgeEpoch) => edgeEpoch ?? 0;

    /// <summary>Whether the text is a strategy version hash: 64 lower-case hex characters.</summary>
    public static bool IsVersionHash(string? text) => text is { Length: 64 } && HashShape().IsMatch(text);

    /// <summary>The versions in canonical form: distinct, ordinal order. Null when any is not a version hash or the count is 0 or above the maximum.</summary>
    public static IReadOnlyList<string>? Canonical(IEnumerable<string>? versions)
    {
        if (versions is null)
        {
            return null;
        }

        var list = versions.Select(v => v?.Trim()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return list.Count is 0 or > MaxVersions || list.Any(v => !IsVersionHash(v)) ? null : [.. list.OfType<string>()];
    }

    /// <summary>SHA-256 of the canonical versions joined by a line feed: the identity of the set.</summary>
    public static string SetHash(IReadOnlyList<string> canonicalVersions)
    {
        ArgumentNullException.ThrowIfNull(canonicalVersions);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', canonicalVersions))));
    }

    /// <summary>
    /// The default set: the strategy version of every <c>cells.eligible/...</c> component of the snapshot (the keys of the paper fingerprint, <c>ConfigSnapshotSource</c>). A component whose version could not be resolved (<c>unknown</c>) is left out, so an unreadable
    /// input never joins the set. Null when the snapshot is missing or unreadable, or when no version is left: the shadow record then writes nothing (fail closed).
    /// </summary>
    public static IReadOnlyList<string>? Default(string? snapshotComponentsJson)
    {
        if (snapshotComponentsJson is null || ConfigFingerprint.FromJson(snapshotComponentsJson) is not { } fingerprint)
        {
            return null;
        }

        return Canonical(fingerprint.Values.Where(p => p.Key.StartsWith(PaperConfigKeys.CellsEligible, StringComparison.Ordinal)).Select(p => p.Value).Where(IsVersionHash));
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex HashShape();
}
