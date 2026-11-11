using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Replay;

/// <summary>The market facts of a session: the gate and its note, the correlations of the ranked instruments, and the instrument facts by upper-case symbol.</summary>
/// <param name="Gate">The market gate input; null when none can be formed.</param>
/// <param name="MarketNote">What the breadth is.</param>
/// <param name="Correlations">Correlations of the daily returns up to the close of the session; null when not computable.</param>
/// <param name="Instruments">Sector and baskets by symbol.</param>
public sealed record ReplayMarket(MarketGateInput? Gate, string? MarketNote, CorrelationTable? Correlations, IReadOnlyDictionary<string, InstrumentFacts> Instruments);
