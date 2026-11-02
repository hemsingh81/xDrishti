using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Filings;

/// <summary>The deterministic id of an envelope (P15-02): the first 16 bytes of SHA-256 over <c>instrument_id|kind|period_end or ""|content_sha256</c>, so replays and any insertion order give the same id.</summary>
public static class FilingId
{
    public static Guid Create(Guid instrumentId, FilingKind kind, DateOnly? periodEnd, string contentSha256)
    {
        ArgumentException.ThrowIfNullOrEmpty(contentSha256);
        var text = string.Create(CultureInfo.InvariantCulture, $"{instrumentId:D}|{FilingKinds.Code(kind)}|{(periodEnd is { } p ? p.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty)}|{contentSha256}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return new Guid(hash.AsSpan(0, 16), bigEndian: true);
    }
}
