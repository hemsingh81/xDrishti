using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>
/// How a changelog row names the kill proposal that caused a pause (spec CF-12, ADR 0219): the evidence id <c>kill-proposal:{id}</c>. A pause so marked is lifted only by the re-enable act (owner, fresh password), never by a revert, so the revert
/// handler refuses a row whose evidence starts with <see cref="ProposalPrefix"/>.
/// </summary>
public static class KillEvidence
{
    /// <summary>The start of the evidence id.</summary>
    public const string ProposalPrefix = "kill-proposal:";

    /// <summary>The evidence id of a proposal.</summary>
    public static string ForProposal(long id) => ProposalPrefix + id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether any evidence id names a kill proposal.</summary>
    public static bool NamesProposal(IEnumerable<string> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return evidence.Any(e => e.StartsWith(ProposalPrefix, StringComparison.Ordinal));
    }

    /// <summary>The proposal ids the evidence names, in order.</summary>
    public static IReadOnlyList<long> ProposalIds(IEnumerable<string> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        return [.. evidence
            .Where(e => e.StartsWith(ProposalPrefix, StringComparison.Ordinal))
            .Select(e => long.TryParse(e.AsSpan(ProposalPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0L)
            .Where(id => id > 0)];
    }
}
