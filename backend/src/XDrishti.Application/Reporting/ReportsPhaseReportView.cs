namespace XDrishti.Application.Reporting;

/// <summary>The body of the P8 phase check (spec P8-17).</summary>
/// <param name="Headline">The headline; MET only on real evidence for every counted criterion.</param>
/// <param name="Criteria">Criteria 1 to 10 of P8-00, numbered as there.</param>
/// <param name="Answer">What the What-works view answers on the default set.</param>
/// <param name="Timings">The timed reads, cold and warm.</param>
/// <param name="Runs">Stored runs.</param>
/// <param name="Trades">Trades in the default set.</param>
/// <param name="BasketsWithTrades">Baskets with trades in the default set.</param>
/// <param name="BuiltReports">The reports the check called.</param>
/// <param name="Limits">The honest limits; always printed.</param>
public sealed record ReportsPhaseReportView(
    string Headline,
    IReadOnlyList<LearningPhaseCriterionView> Criteria,
    ReportsPhaseAnswerView Answer,
    IReadOnlyList<ReportsPhaseTimingView> Timings,
    int Runs,
    int Trades,
    int BasketsWithTrades,
    IReadOnlyList<string> BuiltReports,
    IReadOnlyList<string> Limits);
