using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The pre-registered baseline of the announcement set (P15-07): a pure lookup from the exchange's category to <c>material</c> or <c>non_material</c>. A category the file does not list is <c>non_material</c>. The file is committed before the first sample and its SHA-256
/// is written into the sample, the manifest and the eval fingerprint, so a baseline written after seeing labels is detected. Names are compared after trimming, collapsing spaces and ignoring case; never by substring.
/// </summary>
public sealed class AnnouncementCategoryBaseline
{
    public const string Material = "material";

    public const string NonMaterial = "non_material";

    private readonly Dictionary<string, string> _map;

    private AnnouncementCategoryBaseline(Dictionary<string, string> map, string sha256)
    {
        _map = map;
        Sha256 = sha256;
    }

    /// <summary>SHA-256 (lowercase hex) of the file text with LF line endings.</summary>
    public string Sha256 { get; }

    public int Count => _map.Count;

    public static string HashOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ReplaceLineEndings("\n"))));
    }

    /// <summary>Reads <c>{"version":1,"categories":{"name":"material"|"non_material"}}</c>; the first problem is returned as a validation error (code <c>eval.baseline_invalid</c>).</summary>
    public static Result<AnnouncementCategoryBaseline> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != 1
                || !root.TryGetProperty("categories", out var categories) || categories.ValueKind != JsonValueKind.Object)
            {
                return Invalid("it must be {\"version\":1,\"categories\":{...}}");
            }

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var entry in categories.EnumerateObject())
            {
                var label = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
                var key = Key(entry.Name);
                if (key.Length == 0 || label is not (Material or NonMaterial))
                {
                    return Invalid("each category maps to material or non_material");
                }

                if (!map.TryAdd(key, label))
                {
                    return Invalid("a category is listed twice");
                }
            }

            return Result.Success(new AnnouncementCategoryBaseline(map, HashOf(json)));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return Invalid("it is not valid JSON");
        }
    }

    /// <summary>The baseline's class for a category: the listed one, else <c>non_material</c>.</summary>
    public string Predict(string? category) => category is not null && _map.TryGetValue(Key(category), out var label) ? label : NonMaterial;

    public bool IsMaterial(string? category) => Predict(category) == Material;

    private static string Key(string name) => string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant();

    private static Result<AnnouncementCategoryBaseline> Invalid(string why) => Result.Failure<AnnouncementCategoryBaseline>(Error.Validation("eval.baseline_invalid", "The announcement baseline file is not valid: " + why + "."));
}
