using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>The P6 phase check as a read (P8-04, ADR 0103). It takes no parameter: the replay seconds are never accepted from a client.</summary>
public sealed record GetLearningPhaseCheckQuery : IQuery<Result<LearningPhaseCheckView>>;
