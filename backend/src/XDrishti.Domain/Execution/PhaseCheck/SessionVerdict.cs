namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>A session with its class and, for a broken one, the reasons (<c>break:position_mismatch</c>, <c>final_missing</c>, <c>unconfirmed_entry</c> and the like).</summary>
public sealed record SessionVerdict(SessionFacts Facts, SessionClass Class, IReadOnlyList<string> Reasons);
