using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XDrishti.Domain.Paper;

/// <summary>
/// The fingerprint of the configuration a paper count is measured against (spec P10-05, ADR 0119): SHA-256 of the canonical JSON of the flat components, keys in ordinal order. The same components in any order give the same hash; any changed value
/// changes it. A component whose name looks like a secret is dropped (never stored, never hashed).
/// </summary>
public sealed partial class ConfigFingerprint
{
    private ConfigFingerprint(SortedDictionary<string, string> values, string json, int dropped)
    {
        Values = values;
        CanonicalJson = json;
        Dropped = dropped;
        Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    /// <summary>The components by key, in ordinal order.</summary>
    public IReadOnlyDictionary<string, string> Values { get; }

    /// <summary>The canonical JSON the hash is taken over.</summary>
    public string CanonicalJson { get; }

    /// <summary>The 64-character lower-case hex SHA-256 of <see cref="CanonicalJson"/>.</summary>
    public string Hash { get; }

    /// <summary>How many components were left out because their name looks like a secret.</summary>
    public int Dropped { get; }

    /// <summary>Builds the fingerprint. Two components with one key are a programming error.</summary>
    public static ConfigFingerprint Create(IEnumerable<ConfigComponent> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var dropped = 0;
        foreach (var c in components)
        {
            if (IsSecretName(c.Key))
            {
                dropped++;
                continue;
            }

            if (!sorted.TryAdd(c.Key, c.Value))
            {
                throw new ArgumentException("Two configuration components have the key " + c.Key + ".", nameof(components));
            }
        }

        return new ConfigFingerprint(sorted, Write(sorted), dropped);
    }

    /// <summary>The fingerprint of stored components JSON, or null when the text is not an object of strings.</summary>
    public static ConfigFingerprint? FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || doc.RootElement.EnumerateObject().Any(p => p.Value.ValueKind != JsonValueKind.String))
            {
                return null;
            }

            return Create(doc.RootElement.EnumerateObject().Select(p => new ConfigComponent(p.Name, p.Value.GetString()!)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Whether a key names a secret (token, key material, password, credential). Such components are never part of the fingerprint.</summary>
    public static bool IsSecretName(string key) => SecretName().IsMatch(key);

    private static string Write(SortedDictionary<string, string> values)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in values)
            {
                writer.WriteString(key, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    [GeneratedRegex("api[_-]?key|private[_-]?key|secret|token|password|passwd|credential", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();
}
