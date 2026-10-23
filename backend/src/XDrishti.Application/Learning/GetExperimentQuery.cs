using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>One recorded experiment in full (P6-11).</summary>
/// <param name="Id">The ledger id.</param>
public sealed record GetExperimentQuery(long Id) : IQuery<Result<ExperimentView>>;
