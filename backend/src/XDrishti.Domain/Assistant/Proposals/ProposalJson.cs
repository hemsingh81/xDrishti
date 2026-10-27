using System.Text.Json;

namespace XDrishti.Domain.Assistant.Proposals;

/// <summary>The stored JSON of a proposal's payload, evidence and checks (spec P9-08 Data): camel case, written and read in one place so the row and the view agree.</summary>
public static class ProposalJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Payload(ProposalPayload payload) => payload switch
    {
        HypothesisPayload h => JsonSerializer.Serialize(new { baselineRunKey = h.BaselineRunKey, cellKey = h.CellKey, knob = new { name = h.KnobName, kind = h.KnobKind, baselineValue = h.BaselineValue, candidateValue = h.CandidateValue } }, Options),
        BasketPayload b => JsonSerializer.Serialize(new { basketId = b.BasketId, symbol = b.Symbol }, Options),
        _ => throw new ArgumentException("Unknown payload.", nameof(payload)),
    };

    /// <summary>The payload of a stored row, or null when the stored text is not of the kind's shape (a damaged row is never acted on).</summary>
    public static ProposalPayload? ReadPayload(string kind, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (kind == ProposalKind.Hypothesis)
            {
                var knob = root.GetProperty("knob");
                return new HypothesisPayload(root.GetProperty("baselineRunKey").GetString()!, root.GetProperty("cellKey").GetString()!, knob.GetProperty("name").GetString()!, knob.GetProperty("kind").GetString()!, knob.GetProperty("baselineValue").GetString()!, knob.GetProperty("candidateValue").GetString()!);
            }

            return ProposalKind.IsBasket(kind) ? new BasketPayload(root.GetProperty("basketId").GetGuid(), root.GetProperty("symbol").GetString()!) : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }

    public static string Evidence(ProposalEvidence evidence) => JsonSerializer.Serialize(evidence, Options);

    public static string Checks(IReadOnlyList<ProposalCheck> checks) => JsonSerializer.Serialize(checks, Options);

    public static ProposalEvidence ReadEvidence(string json) =>
        TryRead<ProposalEvidence>(json) ?? new ProposalEvidence("unavailable", [], null, 0, [], ["The stored evidence could not be read."]);

    public static IReadOnlyList<ProposalCheck> ReadChecks(string json) => TryRead<List<ProposalCheck>>(json) ?? [];

    private static T? TryRead<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
