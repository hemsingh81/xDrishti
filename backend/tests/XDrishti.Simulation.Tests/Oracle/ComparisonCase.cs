using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The candidates of one instrument and side that are walked under one set of rules, in the two vocabularies: the engine's inputs and the oracle's. Whichever side the case came from, the other is derived by <see cref="OracleAdapter"/>;
/// the round-trip tests hold that derivation to account. <paramref name="EngineResults"/> is set when the engine's answer is not to be recomputed but read (the replay family: the outcomes the simulator produced).
/// </summary>
internal sealed record CaseGroup(IReadOnlyList<EntryCandidate> EngineCandidates, WalkRules EngineRules, OracleCase Oracle, IReadOnlyList<WalkOutcome>? EngineResults = null);

/// <summary>One series of a generator, with the fill mode it is compared in; <see cref="Bars"/> are the engine's minutes (the oracle's are in each group).</summary>
internal sealed record ComparisonCase(string Generator, long Seed, int Category, string Mode, IReadOnlyList<Bar> Bars, IReadOnlyList<CaseGroup> Groups);
