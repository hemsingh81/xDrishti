namespace XDrishti.Application.Lab;

/// <summary>One development row the auto unlock reads: <c>pass</c>, <c>fail</c> or <c>unavailable</c>.</summary>
public sealed record AutoRow(string Name, string Status);

/// <summary>
/// The precondition of the auto unlock (spec CF-06, D6 and review fix 5): every development row of the stored verdict is a pass, judged before the hold-out. The rows are the ones that exist today (the outcome, the six gates, effective n, the cost stress
/// and the deflated Sharpe probability); CF-11's scorecard replaces this list. A row with no figure is <c>unavailable</c> and never counts as a pass.
/// </summary>
public static class HoldOutAutoGate
{
    /// <summary>The rows of the verdict plus <c>window_clear</c>: the run's window must end before the block starts, else the look cannot pass and must not take the block's one fresh slot. No window or no block is unavailable.</summary>
    public static IReadOnlyList<AutoRow> Rows(LabVerdictView verdict, DateOnly? runTo, DateOnly? blockStart) =>
        [.. DevelopmentRows(verdict), new("window_clear", runTo is { } to && blockStart is { } start ? (to < start ? "pass" : "fail") : "unavailable")];

    /// <summary>The development rows of the verdict alone (a cheap check before the stores are read).</summary>
    public static bool DevelopmentPass(LabVerdictView verdict) => DevelopmentRows(verdict).All(r => r.Status == "pass");

    private static IReadOnlyList<AutoRow> DevelopmentRows(LabVerdictView verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        return
        [
            new("verdict", verdict.Outcome == "PASS" ? "pass" : "fail"),
            new("gates", verdict.Gates.Count == 0 ? "unavailable" : verdict.Gates.All(g => g.Passed) ? "pass" : "fail"),
            new("effective_n", verdict.Evidence.Dependence?.EnoughByNEff is { } enough ? (enough ? "pass" : "fail") : "unavailable"),
            new("cost_stress", verdict.CostStress is null ? "unavailable" : verdict.CostStress.Tradable),
            new("deflated_sharpe", verdict.DeflatedSharpe is { Probability: { } p } d ? (p >= d.MinProbability ? "pass" : "fail") : "unavailable"),
        ];
    }

    /// <summary>True only when every row, the window row included, is a pass.</summary>
    public static bool AllPass(LabVerdictView verdict, DateOnly? runTo, DateOnly? blockStart) => Rows(verdict, runTo, blockStart).All(r => r.Status == "pass");
}
