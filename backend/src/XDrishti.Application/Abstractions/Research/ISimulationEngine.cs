using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// The simulation engine as the use case reaches it (spec P3-04a, D6; ADR 0039): a port in front of the engine modules, which are pure calculation and are reached from an adapter in Infrastructure, never from Application. Everything
/// the signatures use is a Domain value. The calls are synchronous and do no I/O: the use case reads the bars and the rest through its own ports and hands them over.
/// <para>
/// <see cref="Prepare"/> takes a built-in strategy id: until stored strategies exist (P3-02c, P3-02d) there is no text to give. The port changes then (a strategy spec with id, text and parameters); ADR 0037 records this so that nobody mistakes the id
/// for the final shape.
/// </para>
/// </summary>
public interface ISimulationEngine
{
    /// <summary>The version of the replay rules (<c>SimulationEngine.Version</c>), written in every run record and run key.</summary>
    string EngineVersion { get; }

    /// <summary>The built-in strategies a run can name, with their timeframes and parameters (spec P3-04b2, D3). When stored strategies exist (P3-02d) this list becomes their seed.</summary>
    IReadOnlyList<BuiltInStrategyInfo> BuiltIns { get; }

    /// <summary>
    /// Builds and compiles the strategy. A failure is <c>simulation.strategy_invalid</c> whose fields carry the path (<see cref="FieldError.Field"/>) and the code (<see cref="FieldError.Code"/>) of each problem: an unknown id or parameter,
    /// a definition the compiler refuses, a plan that reads a market context or the expiry days, what the simulator refuses by name.
    /// </summary>
    /// <param name="strategyId">A built-in strategy id (<c>ORB</c>).</param>
    /// <param name="parameters">The parameters by name (empty: the defaults); the names are the strategy's.</param>
    /// <param name="evaluationTimeframe">The timeframe the rules are evaluated on.</param>
    /// <param name="fills">The fill rules; null means <see cref="FillRules.Default"/>.</param>
    /// <param name="exits">The exits to use in place of the built-in's own (P4-05, D1): null is the built-in as it is, one candidate a plain run of that exit, two or more a comparison on the same entries. The entry, the stop and the time rules are never replaced.</param>
    Result<IPreparedStrategy> Prepare(string strategyId, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills, ExitChoice? exits = null);

    /// <summary>
    /// Replays the prepared strategy over one instrument. Pure and synchronous. A caller error throws (<see cref="ArgumentException"/>, <see cref="InvalidOperationException"/>); a handle that this engine did not make throws
    /// <see cref="ArgumentException"/>.
    /// </summary>
    InstrumentReplay Replay(IPreparedStrategy strategy, InstrumentReplayInput input);

    /// <summary>
    /// Replays a prepared comparison (two or more candidates) over one instrument with one scan (P4-05): the replay of each candidate in order and the gated counts. Pure and synchronous; the errors of <see cref="Replay"/> apply.
    /// </summary>
    ComparisonReplay ReplayComparison(IPreparedStrategy strategy, InstrumentReplayInput input);
}
