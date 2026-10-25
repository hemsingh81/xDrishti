using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Audit;

/// <summary>
/// A place in the merged order (newest first by time, then source ordinal descending, then id descending) and the cursor that carries it. The cursor is
/// <c>base64url(at|ordinal|id|check)</c>; the check only catches a garbled or hand-edited value (a forged cursor with a valid check just pages from that place, which is harmless: it reads the same data). Pure.
/// </summary>
public readonly record struct AuditPosition(DateTimeOffset At, AuditSource Source, string Id)
{
    /// <summary>True when this place is shown before or at <paramref name="other"/> (newer time first, then the higher source ordinal). Two places of one source are ordered by the reader that produced them.</summary>
    public bool IsNotAfter(AuditPosition other) => At != other.At ? At > other.At : Source >= other.Source;

    public string Encode()
    {
        var payload = string.Create(CultureInfo.InvariantCulture, $"{At.UtcTicks}|{(int)Source}|{Id}");
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload + "|" + Check(payload)));
    }

    public static bool TryDecode(string? cursor, out AuditPosition position)
    {
        position = default;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 200)
        {
            return false;
        }

        string text;
        try
        {
            text = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        var parts = text.Split('|');
        if (parts.Length != 4
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks is < 0 or > 3155378975999999999L
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
            || !Enum.IsDefined((AuditSource)ordinal))
        {
            return false;
        }

        var source = (AuditSource)ordinal;
        var payload = string.Join('|', parts[0], parts[1], parts[2]);
        if (!AuditSources.IsValidId(source, parts[2]) || !string.Equals(Check(payload), parts[3], StringComparison.Ordinal))
        {
            return false;
        }

        position = new AuditPosition(new DateTimeOffset(ticks, TimeSpan.Zero), source, parts[2]);
        return true;
    }

    private static string Check(string payload) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("audit-cursor:" + payload)).AsSpan(0, 4));
}
