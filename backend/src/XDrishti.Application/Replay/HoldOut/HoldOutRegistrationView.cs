using XDrishti.Domain.Replay.HoldOut;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>The stored result of a registration as served: the verdict, its reason and the figures of both arms (SR-03 D6). Research only; the hindsight label travels with it.</summary>
public sealed record HoldOutResultBody(SystemReplayVerdict Verdict, SystemRunFigures Figures);

/// <summary>The stored result with its identity.</summary>
public sealed record HoldOutRegistrationResultView(string Verdict, string Reason, SystemRunFigures Figures, IReadOnlyList<Guid> RunIds, string EngineVersions, string Mode, string Actor, DateTimeOffset At);

/// <summary>
/// A registration as served (SR-03): the frozen hash and the inputs it fixed, the rule first, the state and, when it ran, the result. <c>State</c> is <c>waiting</c>, <c>superseded</c>, <c>late</c> (stored on or after the block began), <c>drift</c> (a stored input no longer resolves; read live), <c>claimed</c> (the look is taken
/// and the run follows) or <c>run</c> (the result is stored). The hindsight text is always present.
/// </summary>
public sealed record HoldOutRegistrationView(
    Guid Id,
    DateOnly BlockStart,
    DateOnly BlockEnd,
    string ConfigHash,
    string Mode,
    string Actor,
    DateTimeOffset CreatedAt,
    string State,
    string? DriftReason,
    SystemReplayPassRule Rule,
    string CostSheetId,
    string CostSheetRatesHash,
    decimal Capital,
    int Instruments,
    string EngineVersion,
    string SettingsHash,
    string Hindsight,
    HoldOutRegistrationResultView? Result)
{
    /// <summary>The label that goes with every figure of this view.</summary>
    public const string HindsightText = "Unseen by the system; the owner may have seen these prices elsewhere. Research only: no readiness figure reads it.";
}
