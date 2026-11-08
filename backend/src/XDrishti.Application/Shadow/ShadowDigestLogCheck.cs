using System.Globalization;
using XDrishti.Application.Abstractions.Shadow;

namespace XDrishti.Application.Shadow;

/// <summary>Compares the digest log outside the database with the chain in it (ADR 0232): every logged (last session, digest) must equal that session's stored digest. A database owner who recomputes the chain cannot also rewrite this file unseen.</summary>
internal static class ShadowDigestLogCheck
{
    /// <summary>The problem found, or null when the log agrees with the chain.</summary>
    /// <param name="log">The log as read.</param>
    /// <param name="digests">The stored digest of every session.</param>
    /// <param name="earlierExport">Whether the service has a successful export in its run history (so a missing or empty log is a deletion).</param>
    public static string? Problem(ShadowDigestLogRead log, IReadOnlyDictionary<DateOnly, string> digests, bool earlierExport)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(digests);
        if (log.Problem is { } broken)
        {
            return broken;
        }

        if (log.Lines.Count == 0)
        {
            return earlierExport ? "The digest log is missing or empty although the service exported before: it was deleted or emptied." : null;
        }

        foreach (var line in log.Lines)
        {
            if (!digests.TryGetValue(line.LastSession, out var stored))
            {
                return string.Create(CultureInfo.InvariantCulture, $"The digest log names the session {line.LastSession:yyyy-MM-dd}, which the chain does not hold.");
            }

            if (!string.Equals(stored, line.Digest, StringComparison.Ordinal))
            {
                return string.Create(CultureInfo.InvariantCulture, $"The digest of the session {line.LastSession:yyyy-MM-dd} in the log differs from the stored one: the chain was rewritten after it was logged.");
            }
        }

        return null;
    }
}
