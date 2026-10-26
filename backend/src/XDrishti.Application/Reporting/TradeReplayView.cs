namespace XDrishti.Application.Reporting;

/// <summary>One stored simulated trade drawn on its own bars (P8-11): the trade, its legs and costing, the bars of its window at one timeframe with the coherence check, the decision-time features it has, and the evidence block (n = 1). Read only.</summary>
/// <param name="RunKey">The run.</param>
/// <param name="Ordinal">The trade's place in the run.</param>
/// <param name="OpenAtEnd">The trade was still open at the end of the data.</param>
/// <param name="TradeKey">The stored signal key.</param>
/// <param name="Basis">gross, or net only when the owner confirmed the charge sheet the run was costed with.</param>
/// <param name="Trade">The stored facts.</param>
/// <param name="Legs">The fills, in the record's order; empty for a trade that entered and left in one fill each.</param>
/// <param name="Costing">The costing, or null for a gross run.</param>
/// <param name="Chart">The bars and the coherence.</param>
/// <param name="Features">The stored decision-time features.</param>
/// <param name="Evidence">The evidence block.</param>
/// <param name="Limits">What this picture does not show.</param>
public sealed record TradeReplayView(
    string RunKey,
    int Ordinal,
    bool OpenAtEnd,
    string TradeKey,
    string Basis,
    ReplayTradeView Trade,
    IReadOnlyList<ReplayLegView> Legs,
    ReplayCostingView? Costing,
    ReplayChartView Chart,
    ReplayFeaturesView Features,
    ReportEvidenceView Evidence,
    IReadOnlyList<string> Limits);
