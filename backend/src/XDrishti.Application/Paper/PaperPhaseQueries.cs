using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Paper;

namespace XDrishti.Application.Paper;

/// <summary>The paper phase check (spec P10-06, ADR 0123): the P10 exit judged from stored records. Read only.</summary>
/// <param name="Through">The last session to count; rounded down to a trading session; null for the last closed session.</param>
/// <param name="EpochId">The epoch to judge; null for the newest.</param>
public sealed record EvaluatePaperPhaseCheckQuery(DateOnly? Through, long? EpochId) : IQuery<Result<PaperPhaseCheckView>>;

/// <summary>The paper phase check as the verb prints it and the endpoint returns it.</summary>
/// <param name="Report">The criteria, the weeks, the epochs, what is printed and the limits.</param>
/// <param name="Text">The printable report, limits included.</param>
public sealed record PaperPhaseCheckView(PaperPhaseReport Report, string Text);
