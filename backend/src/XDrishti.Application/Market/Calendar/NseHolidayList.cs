using System.Globalization;
using System.Text.Json;
using XDrishti.Application.Abstractions.Market;

namespace XDrishti.Application.Market.Calendar;

/// <summary>One holiday of NSE's list.</summary>
internal sealed record ListedHoliday(DateOnly Date, string Description);

/// <summary>What was read from NSE's list: the holidays of the cash-market segment, how many entries could not be read, or why nothing could be read at all.</summary>
internal sealed record HolidayParse(IReadOnlyList<ListedHoliday> Holidays, int Unreadable, string? Failure);

/// <summary>
/// Reads NSE's public trading-holiday list (<c>holiday-master?type=trading</c>) defensively: the format is not documented and can change, so anything unexpected is a
/// skipped entry or a plain failure message, never an exception. Expected: an object with one array per segment (<c>CM</c> is the cash market), each entry having
/// <c>tradingDate</c> (<c>dd-MMM-yyyy</c>) and <c>description</c>. Property names are matched ignoring case; ISO dates are accepted too.
/// </summary>
internal static class NseHolidayList
{
    public const string Segment = "CM";

    /// <summary>The most entries read from the segment (NSE lists about 15 a year).</summary>
    public const int MaxEntries = 500;

    public const int MaxDescriptionLength = 120;

    private static readonly string[] DateFormats = ["dd-MMM-yyyy", "d-MMM-yyyy", "yyyy-MM-dd"];

    public static async ValueTask<HolidayParse> ParseAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        try
        {
            using var document = await JsonDocument.ParseAsync(content, new JsonDocumentOptions { MaxDepth = 16 }, cancellationToken);
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return Failed("NSE's holiday list is not valid JSON.");
        }
        catch (SourceReadException ex)
        {
            return Failed(ex.Message);
        }
    }

    internal static HolidayParse Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Failed("NSE's holiday list does not have the expected shape.");
        }

        JsonElement? entries = null;
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, Segment, StringComparison.OrdinalIgnoreCase))
            {
                entries = property.Value;
                break;
            }
        }

        if (entries is not { ValueKind: JsonValueKind.Array } list)
        {
            return Failed("NSE's holiday list has no cash-market (CM) section.");
        }

        var holidays = new Dictionary<DateOnly, ListedHoliday>();
        var unreadable = 0;
        var seen = 0;
        foreach (var entry in list.EnumerateArray())
        {
            if (++seen > MaxEntries)
            {
                unreadable++;
                continue;
            }

            if (TryReadEntry(entry, out var holiday))
            {
                holidays.TryAdd(holiday.Date, holiday);
            }
            else
            {
                unreadable++;
            }
        }

        return new HolidayParse([.. holidays.Values.OrderBy(h => h.Date)], unreadable, null);
    }

    private static bool TryReadEntry(JsonElement entry, out ListedHoliday holiday)
    {
        holiday = null!;
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? date = null;
        string? description = null;
        foreach (var property in entry.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (string.Equals(property.Name, "tradingDate", StringComparison.OrdinalIgnoreCase))
            {
                date = property.Value.GetString();
            }
            else if (string.Equals(property.Name, "description", StringComparison.OrdinalIgnoreCase))
            {
                description = property.Value.GetString();
            }
        }

        if (!DateOnly.TryParseExact(date?.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return false;
        }

        holiday = new ListedHoliday(parsed, Describe(description));
        return true;
    }

    private static string Describe(string? description)
    {
        var text = new string([.. (description ?? string.Empty).Where(c => !char.IsControl(c))]).Trim();
        return text.Length <= MaxDescriptionLength ? text : text[..MaxDescriptionLength];
    }

    private static HolidayParse Failed(string message) => new([], 0, message);
}
