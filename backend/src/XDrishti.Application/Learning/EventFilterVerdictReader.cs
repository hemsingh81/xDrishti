using System.Globalization;
using System.Text.Json;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Learning;

/// <summary>
/// The real <see cref="IEventFilterVerdict"/> (P15-10, decision 3): the newest stored protocol-F verdict <b>for the N of the effective switch</b> and the asked rule version, with the record id as its reference. A verdict for another N, another rule version or none is null
/// (unavailable), never KEEP. <see cref="EventFilterVerdict.HoldOutConfirmed"/> is true for a hold-out record that is KEEP, and for a development record only when a stored hold-out KEEP names that very record (<c>development_record</c> in its sources): a hold-out that failed, then new data and a
/// new development KEEP, is therefore not confirmed. It lives with the evaluations, not with the selection code: the decision areas name no evaluation type. Reads only; the auto rule that may act on a confirmed KEEP is <c>ApplyEventFilterAutoHandler</c> (P15-06), armed by the owner alone.
/// </summary>
internal sealed class EventFilterVerdictReader(IExtensionEvaluationStore store, IEventFilterSwitch current) : IEventFilterVerdict
{
    private const int Window = 100;

    public async ValueTask<EventFilterVerdict?> NewestAsync(string ruleVersion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleVersion);
        var state = await current.CurrentAsync(cancellationToken);
        var key = ruleVersion + string.Create(CultureInfo.InvariantCulture, $":N={state.ResultsWithinDays}");
        var matching = (await store.ListAsync(ExtensionFamilies.Filter, Window, cancellationToken)).Where(r => r.Protocol == ProtocolF.Protocol && string.Equals(r.VersionKey, key, StringComparison.Ordinal)).ToList();
        if (matching.Count == 0)
        {
            return null;
        }

        var newest = matching[0];
        var confirmed = newest.HoldOutRead
            ? newest.Verdict == "KEEP"
            : newest.Verdict == "KEEP" && matching.Any(r => r.HoldOutRead && r.Verdict == "KEEP" && DevelopmentRecordOf(r) == newest.EvaluationId);
        return new EventFilterVerdict(newest.Verdict, ruleVersion, newest.EvaluationId, confirmed, state.ResultsWithinDays);
    }

    private static string? DevelopmentRecordOf(ExtensionEvaluationRecord record)
    {
        try
        {
            using var document = JsonDocument.Parse(record.InputsJson);
            return document.RootElement.TryGetProperty("sources", out var sources) && sources.TryGetProperty("development_record", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
