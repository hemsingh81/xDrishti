using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>Lists recorded experiments, newest first (P6-11).</summary>
/// <param name="Limit">1 to 200.</param>
public sealed record ListExperimentsQuery(int Limit) : IQuery<Result<IReadOnlyList<ExperimentView>>>;
