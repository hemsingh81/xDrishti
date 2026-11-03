using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>The development verdict of protocol O, the number of development trades the embargo and the purge dropped, and the summary lines the verb prints (every unavailable value is the text <c>unavailable</c>).</summary>
public sealed record ProtocolOResult(ProtocolOutcome Outcome, int EmbargoDropped, IReadOnlyList<string> Lines);
