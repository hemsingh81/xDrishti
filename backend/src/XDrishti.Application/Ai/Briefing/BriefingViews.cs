using System.Text.Json;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Text;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>One finding of the guard, as the API shows it.</summary>
public sealed record BriefingFindingView(string Code, string Token, int Offset, string Words);

/// <summary>One model attempt, as the API shows it. The refused text itself stays in the audit table.</summary>
public sealed record BriefingAttemptView(int N, bool Passed, IReadOnlyList<BriefingFindingView> Findings, int LatencyMs);

/// <summary>The records behind the facts of a briefing.</summary>
public sealed record BriefingEvidenceView(long? PlanId, long? DigestId, IReadOnlyList<string> RunKeys);

/// <summary>A briefing or weekly review text (spec P9-06 Contract). <c>Id</c> is null for a preview, which stores nothing. <c>Source</c> is <c>model</c> or <c>deterministic</c>; <c>Reason</c> is null for the model.</summary>
public sealed record BriefingView(
    long? Id, string Kind, string Subject, int Version, string Source, string? Reason, string Text, string DeterministicText, int WordCount, string FactsHash, string? ModelId, string PromptVersion,
    IReadOnlyList<BriefingAttemptView> Attempts, BriefingEvidenceView Evidence, DateTimeOffset? CreatedAt, bool Repeated)
{
    internal static BriefingView Of(StoredBriefing s, bool repeated)
    {
        var facts = BriefingFactsCodec.Parse(s.FactsJson);
        return new BriefingView(
            s.Id, s.Kind, s.Subject, s.Version, s.Source, s.Reason, s.Text, s.DeterministicText, s.WordCount, BriefingFactsCodec.Hash(s.FactsJson), s.ModelId, s.PromptVersion,
            [.. s.Attempts.Select(a => new BriefingAttemptView(a.Attempt, a.Passed, [.. a.Findings.Select(f => new BriefingFindingView(f.Code, f.Token, f.Offset, GuardCodes.Words(f.Code)))], a.LatencyMs))],
            new BriefingEvidenceView(facts?.PlanId, facts?.DigestId, facts?.RunKeys ?? []), s.CreatedAt, repeated);
    }
}

/// <summary>Reads and hashes the facts block as stored: property order is the record's own, so equal facts give equal bytes.</summary>
internal static class BriefingFactsCodec
{
    public static string Json(BriefingFacts facts) => JsonSerializer.Serialize(facts);

    public static BriefingFacts? Parse(string json) => JsonSerializer.Deserialize<BriefingFacts>(json);

    public static string Hash(string json) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
}

/// <summary>Builds (and stores) one briefing or weekly review; the only write is its own insert-only rows. With the model off, unready or rejected it stores the fixed-format text.</summary>
/// <param name="Kind"><c>daily</c> or <c>weekly</c>.</param>
/// <param name="Subject">The plan date or the ISO week; null = the newest plan's date / this week.</param>
/// <param name="NewVersion">Append version n+1 although the subject has a stored text (the owner's act, from the verb).</param>
/// <param name="ForceDeterministic">Never call the model for this build (the verb's <c>--model off</c>).</param>
/// <param name="Actor"><c>system:briefing</c>, <c>system:weekly-review</c>, <c>cli:xd</c>.</param>
public sealed record BuildBriefingCommand(string Kind, string? Subject, bool NewVersion, bool ForceDeterministic, string Actor) : ICommand;

/// <summary>The fixed-format text of any plan date or week, computed and shown, never stored and never sent to the model.</summary>
public sealed record PreviewBriefingQuery(string Kind, string? Subject) : IQuery<Result<BriefingView>>;

/// <summary>The stored text of a plan date or week (newest version).</summary>
public sealed record GetBriefingQuery(string Kind, string? Subject) : IQuery<Result<BriefingView>>;

/// <summary>The stored texts, newest first.</summary>
public sealed record ListBriefingsQuery(string? Kind, int Limit) : IQuery<Result<IReadOnlyList<BriefingView>>>
{
    public const int DefaultLimit = 14;
}
