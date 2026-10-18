using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation;
using XDrishti.Simulation.Replay;
using XDrishti.Strategies.Compilation;

namespace XDrishti.Infrastructure.Research;

/// <summary>
/// The adapter of <see cref="ISimulationEngine"/> over the simulation library (spec P3-04a, D6; ADR 0039): thin, stateless and without I/O or a clock. <see cref="Prepare"/> builds the definition of a built-in strategy
/// and prepares it (<see cref="BuiltInStrategies.Prepare"/>, over <c>StrategyPreparation</c>), turning every located problem into a field error whose field is the path and whose code is the problem's; <see cref="Replay"/> hands the input to
/// <see cref="InstrumentReplayer"/>. It is a singleton: the prepared strategy belongs to a run and no scoped service is injected here.
/// </summary>
internal sealed class SimulationEngineAdapter : ISimulationEngine
{
    public string EngineVersion => SimulationEngine.Version;

    public Result<IPreparedStrategy> Prepare(string strategyId, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills)
    {
        ArgumentNullException.ThrowIfNull(strategyId);
        ArgumentNullException.ThrowIfNull(parameters);

        var prepared = BuiltInStrategies.Prepare(strategyId, parameters, evaluationTimeframe, fills);
        return prepared.Strategy is { } strategy
            ? Result.Success<IPreparedStrategy>(new PreparedHandle(strategy, Fields(prepared.Problems)))
            : Refused(prepared.Problems);
    }

    public InstrumentReplay Replay(IPreparedStrategy strategy, InstrumentReplayInput input)
    {
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(input);
        if (strategy is not PreparedHandle handle)
        {
            throw new ArgumentException("The strategy was not prepared by this engine.", nameof(strategy));
        }

        return InstrumentReplayer.Replay(handle.Strategy, input);
    }

    private static Result<IPreparedStrategy> Refused(IReadOnlyList<LocatedProblem> problems) =>
        Result.Failure<IPreparedStrategy>(SimulationErrors.StrategyInvalid(Fields(problems)));

    /// <summary>A located problem as a field error: the path is the field, the code of the problem is the code.</summary>
    internal static List<FieldError> Fields(IReadOnlyList<LocatedProblem> problems) =>
        [.. problems.Select(p => new FieldError(p.Path, p.Problem.Message, Code: p.Problem.Code))];

    /// <summary>The handle the use case holds: the prepared strategy of the library and the warnings of its compile.</summary>
    private sealed class PreparedHandle(PreparedStrategy strategy, IReadOnlyList<FieldError> warnings) : IPreparedStrategy
    {
        public PreparedStrategy Strategy { get; } = strategy;

        public StrategyRecord Record => Strategy.Record;

        public IReadOnlyList<FieldError> Warnings { get; } = warnings;
    }
}
