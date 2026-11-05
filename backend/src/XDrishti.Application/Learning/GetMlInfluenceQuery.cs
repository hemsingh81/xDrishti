using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>How the influence predicate stands for one registered model (CF-10 part 10c-2). Read only: it records nothing.</summary>
/// <param name="Id">The registry id.</param>
public sealed record GetMlInfluenceQuery(long Id) : IQuery<Result<MlInfluenceView>>;
