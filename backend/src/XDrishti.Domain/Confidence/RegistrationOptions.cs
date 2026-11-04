using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Registration</c> settings of CF-06 (ADR 0207). <paramref name="Enforce"/> (default false): off, a full or robust run without a registration only carries the warning <c>not_registered</c>; on, it is refused with 422 <c>lab.not_registered</c>.
/// <paramref name="CountInFactor"/> (default false): off, the verdict keeps judging with the number of distinct strategy hashes and the honest number of looks is shown beside it; on, the honest number is the one the gates are tightened by. Both are the owner's switches.
/// </summary>
public sealed record RegistrationOptions(bool Enforce = false, bool CountInFactor = false)
{
    /// <summary>The text of the values, kept with a stored figure.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"registration/enforce={(Enforce ? "on" : "off")}/count_in_factor={(CountInFactor ? "on" : "off")}");

    /// <summary>The snapshot hash: the lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());
}
