using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The canonical text of a csv row or group (P15-04) and its SHA-256: what the envelope's content hash is made of, so the same row in two files, in any line order, is one envelope. The text holds the instrument id (never the symbol's spelling), the period, the values and the
/// time to the microsecond (or <c>unavailable:reason</c>); it never holds a file name, a line number or an ingest time.
/// </summary>
public static class RowCanonical
{
    public static string Meeting(Guid instrumentId, MeetingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return string.Create(CultureInfo.InvariantCulture, $"board_meeting|{instrumentId:D}|{Date(row.PeriodEnd)}|{Date(row.MeetingDate)}|{row.Purpose}|{row.Status}|{Time(row.Time, row.Intimation.Reason)}");
    }

    public static string Shareholding(Guid instrumentId, ShareholdingGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);
        var lines = string.Join(";", group.Lines.OrderBy(l => Array.IndexOf(ShareholdingCsv.Categories, l.Category)).Select(l => string.Create(CultureInfo.InvariantCulture, $"{l.Category}={Number(l.Pct)}/{Number(l.PledgedPct)}")));
        return string.Create(CultureInfo.InvariantCulture, $"shareholding|{instrumentId:D}|{Date(group.PeriodEnd)}|{Time(group.Time, group.Broadcast.Reason)}|{lines}");
    }

    /// <summary>
    /// The stored text of an announcement (P15-07): one JSON object with a fixed key order, so the same row in two files is the same bytes. It holds the instrument id (never the symbol's spelling), the time to the microsecond (or <c>unavailable:reason</c>), the category, the
    /// subject and the text. <see cref="AnnouncementBody.Read"/> reads it back.
    /// </summary>
    public static string Announcement(Guid instrumentId, AnnouncementRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        using var stream = new MemoryStream();
        using (var json = new System.Text.Json.Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("kind", "announcement");
            json.WriteString("instrument", instrumentId.ToString("D"));
            json.WriteString("time", Time(row.Time, row.Broadcast.Reason));
            json.WriteString("category", row.Category);
            json.WriteString("subject", row.Subject);
            json.WriteString("text", row.Text);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Sha256(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static int Bytes(string canonical)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        return Encoding.UTF8.GetByteCount(canonical);
    }

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Number(decimal? value) => value is { } v ? v.ToString("0.####", CultureInfo.InvariantCulture) : "-";

    private static string Time(DateTimeOffset? time, string? reason) =>
        time is { } t ? t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture) : "unavailable:" + (reason ?? "missing");
}
