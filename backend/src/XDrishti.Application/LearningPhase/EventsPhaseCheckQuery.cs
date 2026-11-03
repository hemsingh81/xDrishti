using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.LearningPhase;

/// <summary>The P15 phase check (spec P15-12): the exit "features and filters improve OOS or are dropped" read from stored verdicts. Read only.</summary>
/// <param name="Cut">The families the owner leaves out of the overall (listed, not counted); unknown names are refused.</param>
public sealed record EventsPhaseCheckQuery(IReadOnlyList<string> Cut) : IQuery<Result<EventsPhaseCheckView>>;
