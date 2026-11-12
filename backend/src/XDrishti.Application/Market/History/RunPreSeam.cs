using System.Globalization;
using System.Text.Json;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Market.History;

/// <summary>
/// What a stored run says about the pre-seam bars it admitted (CF-04d): the <c>pre_seam</c> member of each instrument of its record text. A later reader follows the record, never its own decision: an instrument without the member was run on the cut bars with the plain factors,
/// whatever the admission switch says now. Missing or unreadable text gives <c>null</c>, which is not "admitted nothing": a reader then treats every instrument that has a seam row as stale (fail closed).
/// </summary>
internal static class RunPreSeam
{
    public static IReadOnlyDictionary<Guid, PreSeamRecord>? Parse(string? recordText)
    {
        if (string.IsNullOrEmpty(recordText))
        {
            return null;
        }

        var map = new Dictionary<Guid, PreSeamRecord>();

        try
        {
            using var document = JsonDocument.Parse(recordText);
            if (!document.RootElement.TryGetProperty("instruments", out var instruments) || instruments.ValueKind != JsonValueKind.Array)
            {
                return null;
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
            return null;
        }

        return map;
    }
}
