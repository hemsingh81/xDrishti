using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The overview of the stored learning records (P6-18).</summary>
public sealed record GetLearningOverviewQuery : IQuery<Result<LearningOverviewView>>;
