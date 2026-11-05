using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// Restates the real trades of a plan with random entries over the stored bars (spec CF-07, ADR 0208). It prepares the strategy and reads the instruments of the plan exactly as <see cref="RunSimulationQuery"/> does, over the window of the request only (no bar after
/// its end is read), and writes nothing. <paramref name="Progress"/> is told the number of instruments finished, after each one.
/// </summary>
public sealed record RunNullWalkQuery(SimulationRequest Request, NullWalkPlan Plan, IProgress<int>? Progress = null) : IQuery<Result<NullWalkRun>>;

/// <summary>What the random-entry walk gave: the draws of every real trade of the plan whose instrument was walked, in plan order.</summary>
/// <param name="Trades">One entry per trade of the plan.</param>
public sealed record NullWalkRun(IReadOnlyList<NullTradeDraws> Trades);
