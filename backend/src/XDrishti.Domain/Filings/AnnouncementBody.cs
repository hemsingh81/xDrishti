using System.Text.Json;

namespace XDrishti.Domain.Filings;

/// <summary>The category, subject and text of a stored announcement (P15-07), read back from the canonical text that <see cref="RowCanonical.Announcement"/> wrote. Third-party text: data, never interpreted.</summary>
public sealed record AnnouncementBody(string Category, string Subject, string Text)
{
    /// <summary>Reads the stored bytes; null when they are not a stored announcement (never throws on bad input).</summary>
    public static AnnouncementBody? Read(ReadOnlySpan<byte> utf8)
    {
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || Get(root, "kind") != "announcement")
            {
                return null;
            }

            var (category, subject, text) = (Get(root, "category"), Get(root, "subject"), Get(root, "text"));
            return category is null || subject is null || text is null ? null : new AnnouncementBody(category, subject, text);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Get(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
