using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The result of <see cref="WalkForwardValidator.Compare"/> (P6-05): every fold, the pooled out-of-sample metrics per scorer, the verdict per candidate and the honest limits that ride with them (P6-00 D5).
/// </summary>
/// <param name="Options">The options used.</param>
/// <param name="Rows">Rows given.</param>
/// <param name="From">Earliest trade date.</param>
/// <param name="To">Latest trade date.</param>
/// <param name="Folds">Every fold, used or not.</param>
/// <param name="Results">The used folds with their metrics.</param>
/// <param name="Pooled">Metrics over the pooled out-of-sample rows of the used folds, by scorer.</param>
/// <param name="Verdicts">One verdict per candidate.</param>
/// <param name="Limits">The sentences that must be printed with the result.</param>
public sealed record ValidationReport(
    ValidationOptions Options,
    int Rows,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ValidationFold> Folds,
    IReadOnlyList<FoldResult> Results,
    IReadOnlyList<ScorerFoldResult> Pooled,
    IReadOnlyList<CandidateVerdict> Verdicts,
    IReadOnlyList<string> Limits)
{
    /// <summary>A fixed plain-text rendering; the same report gives the same text.</summary>
    public string ToText()
    {
        var c = CultureInfo.InvariantCulture;
        var t = new StringBuilder();
        t.Append(c, $"Validation of {Rows} trades, {From:yyyy-MM-dd} to {To:yyyy-MM-dd}, {Results.Count} of {Folds.Count} folds used\n");
        foreach (var r in Results)
        {
            t.Append(c, $"fold {r.Fold.Index} {r.Fold.TestStart:yyyy-MM-dd}..{r.Fold.TestEnd:yyyy-MM-dd} train {r.Fold.TrainCount} (purged {r.Fold.Purged}, embargoed {r.Fold.Embargoed}) test {r.Fold.TestCount} always-take win {r.AlwaysTake.WinRate:F4} meanR {r.AlwaysTake.MeanR:F4}\n");
            foreach (var s in r.Scorers)
            {
                t.Append(c, $"  {s.Name,-12} {Line(s.Metrics)}\n");
            }
        }

        t.Append("pooled\n");
        foreach (var s in Pooled)
        {
            t.Append(c, $"  {s.Name,-12} {Line(s.Metrics)}\n");
        }

        foreach (var v in Verdicts)
        {
            t.Append(c, $"{v.Name}: {v.Outcome} ({v.FoldsBeatingBoth} of {v.UsedFolds} folds, needed {v.FoldsNeeded}; pooled beats both {v.PooledBeatsBoth}; ece {v.PooledEcePp:F2} pp)");
            if (v.Reasons.Count > 0)
            {
                t.Append(" - ").Append(string.Join("; ", v.Reasons));
            }

            t.Append('\n');
        }

        foreach (var l in Limits)
        {
            t.Append(l).Append('\n');
        }

        return t.ToString();
    }

    private static string Line(ProbabilityMetrics m)
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(c, $"n {m.N} logloss {m.LogLoss:F4} brier {m.Brier:F4} auc {(m.Auc is { } a ? a.ToString("F4", c) : "n/a")} ece {m.EcePp:F2}pp lift {(m.Lift is { } l ? l.ToString("F2", c) : "n/a")}");
    }
}
