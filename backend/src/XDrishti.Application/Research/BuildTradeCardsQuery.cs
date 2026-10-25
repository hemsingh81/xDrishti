using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>
/// Reads again the stored 1-minute bars of the instruments that have a chosen trade and returns, for each trade key, what its card needs (spec P3-04c, C2). It is the extension point of P3-09: it renders any given set of trade keys of a
/// run, whoever chose them. An unknown key fails as <c>simulation.invalid</c>; data that changed since the run fails as <c>simulation.cards_stale</c>; an empty list reads nothing.
/// With a <paramref name="Basis"/> (P3-04d) each card also carries the explanation of its trade, recomputed with the engine; a recomputation that does not give the recorded trade fails as <c>simulation.cards_mismatch</c>.
/// </summary>
/// <param name="Basis">What the run held to recompute it; null for a stored run (no explanation).</param>
/// <param name="Run">The run record; the basket is not resolved again, the members of <c>Run.Universe</c> are the universe.</param>
/// <param name="TradeKeys">The <c>SimTrade.Key</c> of each chosen trade; the answer is in this order, each key once.</param>
public sealed record BuildTradeCardsQuery(SimulationRun Run, IReadOnlyList<string> TradeKeys, ExplainBasis? Basis = null) : IQuery<Result<IReadOnlyList<TradeCardData>>>;
