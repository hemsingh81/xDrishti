using System.Globalization;
using System.Text.Json.Nodes;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Domain.Assistant;

namespace XDrishti.Infrastructure.Ai.ReadOnly.EvidenceApi;

/// <summary>
/// <c>get_evidence</c> over the API (AI-01, ADR 0280): the five kinds map to five GETs of the evidence policy. The API's body is handed on unchanged under <c>body</c> (its hindsight labels, costs label, states and the shadow record's own "separate count" statement stay as the API wrote them), after
/// the fixed fence of a replay: research, not evidence. Replay sessions and trades are not offered (row heavy; the owner reads them on the replay screen). An unreachable API is <c>tool.unavailable</c>, never a number.
/// </summary>
internal sealed class ApiEvidenceReader(EvidenceApiClient client) : IAiEvidenceReader
{
    public const string ReplayList = "replay_list";
    public const string ReplayRun = "replay_run";
    public const string ReadinessList = "readiness_list";
    public const string ReadinessCard = "readiness_card";
    public const string ShadowSummary = "shadow_summary";

    /// <summary>The fence of ADR 0272, said with every replay answer: a replay is research, its labels apply to every figure, and it never counts toward a gate.</summary>
    public const string ReplayFence = "Replay is research only, not evidence: every figure is read with its hindsight labels, none counts toward a readiness gate or the paper record, and no order was placed.";

    public const string ReadinessNote = "A readiness item that is unavailable is never a pass; quote its state, not a number.";

    public async Task<AiToolData> GetEvidenceAsync(string kind, string? id, string? strategyId, int? version, CancellationToken cancellationToken)
    {
        string? path;
        switch (kind)
        {
            case ReplayList:
                path = "/api/replay/runs?take=10";
                break;
            case ReplayRun:
                path = Guid.TryParse(id, out var run) ? "/api/replay/runs/" + run.ToString("D") : null;
                break;
            case ReadinessList:
                path = "/api/confidence/scorecards";
                break;
            case ReadinessCard:
                path = !string.IsNullOrEmpty(strategyId) && version is > 0 and <= 999_999 && strategyId.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') && strategyId.Length <= 60
                    ? $"/api/confidence/scorecards/{strategyId}/versions/{version.Value.ToString(CultureInfo.InvariantCulture)}"
                    : null;
                break;
            case ShadowSummary:
                path = "/api/paper/shadow/summary";
                break;
            default:
                return AiToolData.Fail(ToolErrorCodes.InvalidArguments, "Unknown evidence kind.");
        }

        if (path is null)
        {
            return AiToolData.Fail(ToolErrorCodes.InvalidArguments, kind == ReplayRun ? "id: give the replay run id." : "strategyId and version: give the strategy id and a version number.");
        }

        var result = await client.GetAsync(kind, path, cancellationToken);
        if (result.IsFailure)
        {
            return AiToolData.Fail(result.Error.Code, result.Error.Message);
        }

        var replay = kind is ReplayList or ReplayRun;
        var data = new JsonObject { ["kind"] = kind };
        if (replay)
        {
            data["fence"] = ReplayFence;
        }

        data["body"] = result.Value;
        var limits = replay ? new[] { ReplayFence } : kind is ReadinessList or ReadinessCard ? [ReadinessNote] : Array.Empty<string>();
        var evidence = new ToolEvidence("stored results read from the API, unchanged", [kind], null, null, limits, []);
        return AiToolData.OfData(data, evidence, limits);
    }
}
