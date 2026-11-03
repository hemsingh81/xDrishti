using System.Text;
using System.Text.Json;

namespace XDrishti.Domain.Filings.Extraction;

/// <summary>
/// The strict parser of the model's answer (P15-08, schema version 1): one JSON object and nothing else, exactly the six keys of <see cref="AnnouncementSchema"/> once each, enums exact, <c>material</c> a JSON boolean, the reason a string of 1 to 200 characters with no control or
/// direction-override character, the order value a JSON number (not a string) or null. Anything else is invalid with a closed reason from <see cref="ExtractionStates"/>; nothing of an invalid answer is kept. The answer is data: it is read, never evaluated. Pure.
/// </summary>
public static class AnnouncementAnswerParser
{
    private static readonly JsonReaderOptions ReaderOptions = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 4 };

    /// <summary>Parses the text of an answer.</summary>
    public static AnswerParse Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return AnswerParse.Invalid(ExtractionStates.Empty);
        }

        if (text.Contains('\uFFFD', StringComparison.Ordinal) || HasLoneSurrogate(text))
        {
            return AnswerParse.Invalid(ExtractionStates.BadEncoding);
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            var reader = new Utf8JsonReader(bytes, ReaderOptions);
            if (!reader.Read())
            {
                return AnswerParse.Invalid(ExtractionStates.Empty);
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                return AnswerParse.Invalid(ExtractionStates.NotOneObject);
            }

            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;

            // A second value, or any prose after the object, is not an answer (the reader throws on it, which is caught below).
            if (reader.Read())
            {
                return AnswerParse.Invalid(ExtractionStates.NotOneObject);
            }

            var seen = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!AnnouncementSchema.Keys.Contains(property.Name, StringComparer.Ordinal) || !seen.TryAdd(property.Name, property.Value.Clone()))
                {
                    return AnswerParse.Invalid(ExtractionStates.BadKeys);
                }
            }

            if (seen.Count != AnnouncementSchema.Keys.Count)
            {
                return AnswerParse.Invalid(ExtractionStates.BadKeys);
            }

            return Read(seen);
        }
        catch (JsonException)
        {
            // Covers prose before or after, markdown fences, two objects, duplicate commas, depth and truncation.
            return AnswerParse.Invalid(text.TrimStart().StartsWith('{') ? ExtractionStates.NotOneObject : ExtractionStates.NotJson);
        }
    }

    private static AnswerParse Read(Dictionary<string, JsonElement> values)
    {
        var material = values[AnnouncementSchema.Material];
        if (material.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return AnswerParse.Invalid(ExtractionStates.BadValue);
        }

        var category = Word(values[AnnouncementSchema.Category], AnnouncementSchema.Categories);
        var guidance = Word(values[AnnouncementSchema.Guidance], AnnouncementSchema.GuidanceValues);
        var rating = Word(values[AnnouncementSchema.RatingAction], AnnouncementSchema.RatingActions);
        if (category is null || guidance is null || rating is null)
        {
            return AnswerParse.Invalid(ExtractionStates.BadValue);
        }

        var reasonElement = values[AnnouncementSchema.Reason];
        if (reasonElement.ValueKind != JsonValueKind.String)
        {
            return AnswerParse.Invalid(ExtractionStates.BadValue);
        }

        var reason = reasonElement.GetString()!;
        if (reason.Length > AnnouncementSchema.MaxReasonLength)
        {
            return AnswerParse.Invalid(ExtractionStates.TooLongReason);
        }

        if (reason.Trim().Length == 0 || reason.Any(IsForbidden))
        {
            return AnswerParse.Invalid(ExtractionStates.BadValue);
        }

        decimal? order = null;
        var orderElement = values[AnnouncementSchema.OrderValue];
        if (orderElement.ValueKind == JsonValueKind.Number)
        {
            if (!orderElement.TryGetDecimal(out var number) || number < 0 || number > AnnouncementSchema.MaxOrderValue)
            {
                return AnswerParse.Invalid(ExtractionStates.BadValue);
            }

            order = number;
        }
        else if (orderElement.ValueKind != JsonValueKind.Null)
        {
            return AnswerParse.Invalid(ExtractionStates.BadValue);
        }

        return AnswerParse.Ok(new AnnouncementAnswer(material.GetBoolean(), category, reason, order, guidance, rating));
    }

    private static string? Word(JsonElement element, IReadOnlyList<string> allowed) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { } word && allowed.Contains(word, StringComparer.Ordinal) ? word : null;

    // Control characters, the Unicode direction overrides and isolates, and the zero-width characters cannot be shown honestly; the reason is text for a person.
    private static bool IsForbidden(char c) => char.IsControl(c) || c is '\u200B' or '\u200C' or '\u200D' or '\u200E' or '\u200F' or '\u2028' or '\u2029' or '\u2060' or '\uFEFF' || c is >= '\u202A' and <= '\u202E' || c is >= '\u2066' and <= '\u2069';

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(text[i]))
            {
                return true;
            }
        }

        return false;
    }
}
