namespace XDrishti.Application.Abstractions.Ai;

/// <summary>
/// The stored research and readiness results as the assistant's <c>get_evidence</c> reads them (AI-01, ADR 0280): the API's own answers, with their labels and states unchanged. Read only; a refusal or an unreachable API comes back as an error code in <see cref="AiToolData.ErrorCode"/>.
/// </summary>
public interface IAiEvidenceReader
{
    /// <summary><paramref name="kind"/> is replay_list, replay_run (needs <paramref name="id"/>), readiness_list, readiness_card (needs <paramref name="strategyId"/> and <paramref name="version"/>) or shadow_summary.</summary>
    Task<AiToolData> GetEvidenceAsync(string kind, string? id, string? strategyId, int? version, CancellationToken cancellationToken);
}
