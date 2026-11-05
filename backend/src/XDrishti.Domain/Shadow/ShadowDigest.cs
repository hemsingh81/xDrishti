using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Shadow;

/// <summary>
/// The tamper evidence of the shadow record (spec CF-08, D3): each session's digest is the SHA-256 of the previous digest and the sorted row hashes of the session, so a row removed, added or changed after the fact changes every later digest. Pure.
/// </summary>
public static class ShadowDigest
{
    /// <summary>The digest of a session: lower-case hex SHA-256 of <c>prev|hash1,hash2,...</c> with the hashes in ordinal order; the first session has an empty <paramref name="previous"/>. A gap has no hashes.</summary>
    public static string Next(string? previous, IEnumerable<string> rowHashes)
    {
        ArgumentNullException.ThrowIfNull(rowHashes);
        var text = (previous ?? string.Empty) + "|" + string.Join(',', rowHashes.Order(StringComparer.Ordinal));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>
    /// Recomputes the chain from the stored rows and returns the first session (in date order) that does not match, or null when it holds. A session is broken when its previous digest is not the one before, when a stored row hash is not the hash of its stored
    /// columns, or when the stored digest is not the digest of the recomputed hashes. Everything after the first broken session is then also unproven.
    /// </summary>
    public static DateOnly? FirstBroken(IReadOnlyList<ShadowChainSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        string? previous = null;
        foreach (var session in sessions.OrderBy(s => s.SessionDate))
        {
            var hashes = session.Entries.Select(e => e.Entry.RowHash).ToList();
            if (!string.Equals(session.PrevDigest, previous, StringComparison.Ordinal)
                || session.Entries.Any(e => !string.Equals(e.StoredHash, e.Entry.RowHash, StringComparison.Ordinal))
                || !string.Equals(session.Digest, Next(previous, hashes), StringComparison.Ordinal))
            {
                return session.SessionDate;
            }

            previous = session.Digest;
        }

        return null;
    }
}
