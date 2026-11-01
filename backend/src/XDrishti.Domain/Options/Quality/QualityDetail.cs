using System.Text;
using System.Text.Json;

namespace XDrishti.Domain.Options.Quality;

/// <summary>The detail of a result: a flat JSON object of named counts, written by hand (no reflection).</summary>
public static class QualityDetail
{
    public static string Counts(params (string Name, long Value)[] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (name, value) in items)
            {
                writer.WriteNumber(name, value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
