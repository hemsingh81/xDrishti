using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// Runs a strategy with two or more candidate exits over the stored bars on the same entries (spec P4-05): one scan, one walk for each candidate, one run record each and the report that sets them side by side. Nothing is written to any table.
/// The request must carry an <see cref="ExitChoice"/> of two candidates or more; a plain run is <see cref="RunSimulationQuery"/>.
/// </summary>
public sealed record RunComparisonQuery(SimulationRequest Request) : IQuery<Result<ComparisonRun>>;
