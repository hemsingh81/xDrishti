using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// Runs a strategy over the stored 1-minute bars of a set of instruments and returns the complete run record (spec P3-04a). Nothing is written to any table. A run in which every instrument is skipped succeeds and says so; a request that
/// is not valid or a strategy that is refused fails before any bar is read. <paramref name="Progress"/> (optional, P5-02) is told the number of instruments finished, after each one.
/// </summary>
public sealed record RunSimulationQuery(SimulationRequest Request, IProgress<int>? Progress = null, ExplainBasisSink? BasisSink = null) : IQuery<Result<SimulationRun>>;
