using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Learning;

/// <summary>
/// What a stored run says about the pre-seam bars it admitted (CF-04d): the <c>pre_seam</c> member of each instrument of its record text. A later reader follows the record, never its own decision: an instrument without the member was run on the cut bars with the plain factors,
/// whatever the admission switch says now. Unreadable or missing text gives none (the plain read, which is what every run before CF-04d did).
/// </summary>
internal static class RunPreSeam
{
    public static IReadOnlyDictionary<Guid, PreSeamRecord> Parse(string? recordText)
    {
        var map = new Dictionary<Guid, PreSeamRecord>();
        if (string.IsNullOrEmpty(recordText))
        {
            return map;
        }

        try
        {
            using var document = JsonDocument.Parse(recordText);
            if (!document.RootElement.TryGetProperty("instruments", out var instruments) || instruments.ValueKind != JsonValueKind.Array)
            {
                return map;
            }

            foreach (var instrument in instruments.EnumerateArray())
            {
                if (instrument.TryGetProperty("pre_seam", out var pre)
                    && instrument.TryGetProperty("instrument_id", out var id) && Guid.TryParse(id.GetString(), out var guid)
                    && pre.TryGetProperty("seam_date", out var date) && DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var seamDate)
                    && pre.TryGetProperty("basis_sha256", out var basis) && basis.GetString() is { } hash)
                {
                    map[guid] = new PreSeamRecord(seamDate, hash);
                }
            }
        }
        catch (JsonException)
        {
            return new Dictionary<Guid, PreSeamRecord>();
        }

        return map;
    }
}
