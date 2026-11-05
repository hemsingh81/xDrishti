using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// Restates the real signals of a plan with the 20 placebo shapes over the stored bars (spec CF-07 part B, ADR 0209). It prepares the strategy and reads the instruments of the plan exactly as <see cref="RunSimulationQuery"/> does, over the window of the request
/// only (no bar after its end is read), and writes nothing. <paramref name="Progress"/> is told the number of (signal, shape) pairs walked so far.
/// </summary>
public sealed record RunPlaceboWalkQuery(SimulationRequest Request, PlaceboWalkPlan Plan, IProgress<int>? Progress = null) : IQuery<Result<PlaceboWalkRun>>;

/// <summary>What the placebo walk gave: the outcome of every (signal, shape) pair that was walked.</summary>
/// <param name="Outcomes">One entry per pair that has a placement and whose instrument could be read.</param>
public sealed record PlaceboWalkRun(IReadOnlyList<PlaceboOutcome> Outcomes);
