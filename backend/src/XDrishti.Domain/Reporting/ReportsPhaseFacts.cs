namespace XDrishti.Domain.Reporting;

/// <summary>
/// Everything the check reads (spec P8-17). Plain facts: the check is a pure function of them. The server cannot see the interaction test, the stopwatch note, the bundle or the browser pass (the API container mounts no <c>data/</c>), so it
/// passes them as null and the local summary supplies them; a null is never read as a pass.
/// </summary>
/// <param name="Runs">Stored runs.</param>
/// <param name="Trades">Closed trades of the default set.</param>
/// <param name="BasketsWithTrades">Baskets the default set holds trades for.</param>
/// <param name="Answer">The What-works answer on the default set.</param>
/// <param name="Timings">The timed reads.</param>
/// <param name="Reports">What was found in each built report.</param>
/// <param name="MetricsScanned">Metric views looked at.</param>
/// <param name="MetricBreaks">Each metric that breaks the state rule, with the path to the field.</param>
/// <param name="OverlapRefused">The probe set of two overlapping runs was refused with <c>reports.run_overlap</c>.</param>
/// <param name="MixedBasisRefused">The probe set of a gross and a costed run was refused with <c>reports.mixed_basis</c>.</param>
/// <param name="DefaultSetValid">The default set built from the stored runs is valid; null when it could not be read.</param>
/// <param name="Routes">The route table; null when the host cannot show it.</param>
/// <param name="Deterministic">The same request twice gave identical bodies for every built report; null when not read.</param>
/// <param name="DeterminismNote">Which report differed, or what was compared.</param>
/// <param name="SensitiveFieldNames">Field names that hold an account, client, token or password word.</param>
/// <param name="InteractionTestPassed">The interaction test passed at the commit under test; null when no current record exists.</param>
/// <param name="Stopwatch">The owner's stopwatch note for the commit under test; null when none exists.</param>
public sealed record ReportsPhaseFacts(
    int Runs,
    int Trades,
    int BasketsWithTrades,
    ReportsPhaseAnswer Answer,
    IReadOnlyList<ReportsPhaseTiming> Timings,
    IReadOnlyList<ReportsPhaseReportFact> Reports,
    int MetricsScanned,
    IReadOnlyList<string> MetricBreaks,
    bool OverlapRefused,
    bool MixedBasisRefused,
    bool? DefaultSetValid,
    IReadOnlyList<ReportsPhaseRoute>? Routes,
    bool? Deterministic,
    string? DeterminismNote,
    IReadOnlyList<string> SensitiveFieldNames,
    bool? InteractionTestPassed,
    ReportsPhaseStopwatch? Stopwatch);
