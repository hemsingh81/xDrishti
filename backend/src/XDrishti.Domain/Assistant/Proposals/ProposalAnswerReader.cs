using System.Text.Json;
using System.Text.RegularExpressions;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>An answer read as data: the items that have a usable shape, and the refusal codes of the items that have not (counted, never shown as text).</summary>
public sealed record ProposalAnswer(IReadOnlyList<ProposalItem> Items, IReadOnlyList<string> Refused);

/// <summary>
/// Reads the model's answer as data (spec P9-08 AC 2): strict JSON <c>{"items":[...]}</c> with at most three items, each <c>{kind, subject, payload, reason, evidenceRefs}</c>; no field outside the schema, every string bounded, control characters removed. A wrong shape of the whole
/// answer is <c>assistant.answer_shape</c> (the job asks again once); an item with a wrong shape is refused alone. Nothing here trusts a value: the engine checks every one afterwards.
/// </summary>
public static partial class ProposalAnswerReader
{
    public const int MaxItems = 3;
    public const int MaxRefs = 8;
    public const int MaxRefLength = 200;
    public const int MaxSubject = 120;

    private static readonly HashSet<string> ItemFields = new(StringComparer.Ordinal) { "kind", "subject", "payload", "reason", "evidenceRefs" };

    public static Result<ProposalAnswer> Read(string? text)
    {
        var body = StripFence(text ?? string.Empty);
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return Bad("the answer is not an object with one list named items");
            }

            if (items.GetArrayLength() > MaxItems)
            {
                return Bad("more than three items");
            }

            var read = new List<ProposalItem>();
            var refused = new List<string>();
            foreach (var element in items.EnumerateArray())
            {
                var item = ReadItem(element, out var code);
                if (item is null)
                {
                    refused.Add(code!);
                }
                else
                {
                    read.Add(item);
                }
            }

            return Result.Success(new ProposalAnswer(read, refused));
        }
        catch (JsonException)
        {
            return Bad("the answer is not valid JSON");
        }
    }

    private static ProposalItem? ReadItem(JsonElement element, out string? code)
    {
        code = ProposalCodes.PayloadInvalid;
        if (element.ValueKind != JsonValueKind.Object || element.EnumerateObject().Any(p => !ItemFields.Contains(p.Name)))
        {
            return null;
        }

        var kind = Text(element, "kind");
        if (!ProposalKind.IsKnown(kind))
        {
            code = ProposalCodes.KindUnknown;
            return null;
        }

        var reason = Text(element, "reason");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > ProposalWording.MaxReason)
        {
            code = ProposalCodes.ReasonInvalid;
            return null;
        }

        var refs = new List<string>();
        if (!element.TryGetProperty("evidenceRefs", out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() is 0 or > MaxRefs)
        {
            code = ProposalCodes.EvidenceUnresolved;
            return null;
        }

        foreach (var value in list.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 and <= MaxRefLength } reference || reference.Any(char.IsControl))
            {
                code = ProposalCodes.EvidenceUnresolved;
                return null;
            }

            refs.Add(reference);
        }

        if (!element.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object || ReadPayload(kind!, payload) is not { } read)
        {
            code = ProposalCodes.PayloadInvalid;
            return null;
        }

        code = null;
        var subject = Text(element, "subject") ?? string.Empty;
        return new ProposalItem(kind!, Clean(subject.Length > MaxSubject ? subject[..MaxSubject] : subject), read, Clean(reason), [.. refs.Distinct(StringComparer.Ordinal)]);
    }

    private static ProposalPayload? ReadPayload(string kind, JsonElement payload)
    {
        if (kind == ProposalKind.Hypothesis)
        {
            if (payload.EnumerateObject().Any(p => p.Name is not ("baselineRunKey" or "cellKey" or "knob"))
                || Text(payload, "baselineRunKey") is not { Length: 64 } run || !payload.TryGetProperty("knob", out var knob) || knob.ValueKind != JsonValueKind.Object
                || knob.EnumerateObject().Any(p => p.Name is not ("name" or "kind" or "baselineValue" or "candidateValue")))
            {
                return null;
            }

            var cell = Text(payload, "cellKey");
            var name = Text(knob, "name");
            var knobKind = Text(knob, "kind");
            var baseline = Scalar(knob, "baselineValue");
            var candidate = Scalar(knob, "candidateValue");
            return cell is { Length: > 0 and <= 400 } && name is { Length: > 0 and <= 64 } && knobKind is { Length: > 0 and <= 16 } && baseline is not null && candidate is not null
                ? new HypothesisPayload(run, Clean(cell), Clean(name), Clean(knobKind), baseline, candidate)
                : null;
        }

        if (payload.EnumerateObject().Any(p => p.Name is not ("basketId" or "symbol")) || !Guid.TryParseExact(Text(payload, "basketId"), "D", out var basket) || Text(payload, "symbol") is not { } symbol || !Symbol().IsMatch(symbol))
        {
            return null;
        }

        return new BasketPayload(basket, symbol.ToUpperInvariant());
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A value the model may write as a string or as a number; always returned as text.</summary>
    private static string? Scalar(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return text is { Length: > 0 and <= 40 } && !text.Any(char.IsControl) ? text.Trim() : null;
    }

    private static string Clip(string text, int max) => text.Length <= max ? text : text[..max].TrimEnd();

    /// <summary>Control characters removed and markup characters turned to plain ones: the text is shown and stored as text, never as markup.</summary>
    private static string Clean(string text) => UntrustedText.Clean(text).Text.Trim();

    private static string StripFence(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var start = trimmed.IndexOf('\n', StringComparison.Ordinal);
            var end = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (start >= 0 && end > start)
            {
                return trimmed[(start + 1)..end].Trim();
            }
        }

        return trimmed;
    }

    private static Result<ProposalAnswer> Bad(string why) => Result.Failure<ProposalAnswer>(Error.Validation(ProposalCodes.AnswerShape, why));

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9&._-]{0,31}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex Symbol();
}
