using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>
/// The simulation engine as the use case reaches it (spec P3-04a, D6; ADR 0039): a port in front of the engine modules, which are pure calculation and are reached from an adapter in Infrastructure, never from Application. Everything
/// the signatures use is a Domain value. The calls are synchronous and do no I/O: the use case reads the bars and the rest through its own ports and hands them over.
/// <para>
/// <see cref="Prepare"/> takes a built-in strategy id and is kept for the oracle and the tests; a run reads the stored strategy and calls <see cref="PrepareStored"/> with its text and hash (spec P3-02d, ADR 0056). Application hands over
/// strings and numbers only: the engine behind the port reads the text.
/// </para>
/// </summary>
public interface ISimulationEngine
{
    /// <summary>The version of the replay rules (<c>SimulationEngine.Version</c>), written in every run record and run key.</summary>
    string EngineVersion { get; }

    /// <summary>The built-in strategies a run can name, with their timeframes and parameters (spec P3-04b2, D3). The built-ins are also the stored strategies seeded from embedded text (P3-02d); this table is the oracle of that seed and the default timeframes of the verbs.</summary>
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
    /// Prepares a stored strategy (spec P3-02d, D7): reads the text, refuses it with <c>strategy_store.hash_mismatch</c> when it does not give <paramref name="expectedHash"/>, puts the given values in the place of the declared parameters (a name
    /// that is not declared is <c>simulation.unknown_parameter</c>) and prepares it as <see cref="Prepare"/> does, so the run record has the same fields. The failures are those of <see cref="Prepare"/>.
    /// </summary>
    /// <param name="sourceText">The stored text.</param>
    /// <param name="expectedHash">The hash the store holds for it.</param>
    /// <param name="parameters">Values by declared parameter name; empty keeps the declared values.</param>
    /// <param name="evaluationTimeframe">The timeframe the rules are evaluated on.</param>
    /// <param name="fills">The fill rules; null means <see cref="FillRules.Default"/>.</param>
    /// <param name="exits">The exits to use in place of the strategy's own (P4-05); null keeps them.</param>
    Result<IPreparedStrategy> PrepareStored(string sourceText, string expectedHash, IReadOnlyDictionary<string, double> parameters, Timeframe evaluationTimeframe, FillRules? fills, ExitChoice? exits = null);

    /// <summary>Reads and compiles a strategy text on every timeframe it declares and describes it (id, style, hash, canonical text, timeframes, sides, declared parameters); a text that does not read or compile is <c>simulation.strategy_invalid</c> with located fields.</summary>
    Result<StrategyDescription> Describe(string sourceText);

    /// <summary>
    /// Replays the prepared strategy over one instrument. Pure and synchronous. A caller error throws (<see cref="ArgumentException"/>, <see cref="InvalidOperationException"/>); a handle that this engine did not make throws
    /// <see cref="ArgumentException"/>.
    /// </summary>
    InstrumentReplay Replay(IPreparedStrategy strategy, InstrumentReplayInput input);

    /// <summary>
    /// Replays a prepared comparison (two or more candidates) over one instrument with one scan (P4-05): the replay of each candidate in order and the gated counts. Pure and synchronous; the errors of <see cref="Replay"/> apply.
    /// </summary>
    ComparisonReplay ReplayComparison(IPreparedStrategy strategy, InstrumentReplayInput input);

    /// <summary>
    /// Replays as <see cref="Replay"/> does and explains the requested trades (P3-04d): for each request whose decision is a candidate of the scan, the conditions of its side at the decision bar with the values they read, from the replay's
    /// own frame. A request that matches no candidate has no entry in the answer. Pure and synchronous; the errors of <see cref="Replay"/> apply.
    /// </summary>
    ExplainResult Explain(IPreparedStrategy strategy, InstrumentReplayInput input, IReadOnlyList<ExplainRequest> requests);
}
