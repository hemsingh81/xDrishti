using System.Globalization;
using System.Text;

namespace XDrishti.Domain.Learning;

/// <summary>
/// What the validation harness shows about the complexity of the models it judged (CF-10 part 10b, ADR 0215): the ladder rungs with the events behind them, how far each feature moved from its training rows, and whether each feature keeps its direction from fold to fold.
/// It is evidence only: no verdict, eligibility rule or champion rule reads it, and a figure that could not be computed says so.
/// </summary>
/// <param name="TrainRows">Rows of the largest training set (the last used fold's, standing for the final model).</param>
/// <param name="Wins">Wins among them.</param>
/// <param name="Losses">Losses among them.</param>
/// <param name="Folds">Used folds.</param>
/// <param name="Cap">The most features the events allow a logistic model.</param>
/// <param name="Ladder">One assessment per scorer that sits on a rung of interest: the logistic baseline and each candidate.</param>
/// <param name="Shifts">One entry per feature.</param>
/// <param name="Stability">One entry per candidate.</param>
/// <param name="Settings">The text of the settings that produced the figures (<c>LadderOptions.KeyText</c>).</param>
/// <param name="PsiBins">The most bins of the stability index (fewer on small samples).</param>
/// <param name="EffectiveRows">How many independent labels the training rows are worth (the sum of their average uniqueness, CF-10 part 10c-1), or null when it could not be computed.</param>
/// <param name="Paired">The paired bootstrap and sign test of each scorer against the rung below (CF-10 part 10c-1); empty when the paired bootstrap is off.</param>
public sealed record ComplexityReport(
    int TrainRows,
    int Wins,
    int Losses,
    int Folds,
    int Cap,
    IReadOnlyList<LadderAssessment> Ladder,
    IReadOnlyList<FeatureShift> Shifts,
    IReadOnlyList<CandidateStability> Stability,
    string Settings,
    int PsiBins,
    double? EffectiveRows = null,
    IReadOnlyList<RungComparison>? Paired = null)
{
    private const int Listed = 8;

    /// <summary>The events: the smaller class.</summary>
    public int Events => EventsPerVariable.Events(Wins, Losses);

    /// <summary>A fixed plain-text rendering; <paramref name="featureNames"/> names the features when they are the catalog's, otherwise they print as f0, f1, ...</summary>
    public string ToText(IReadOnlyList<string>? featureNames = null)
    {
        var c = CultureInfo.InvariantCulture;
        var t = new StringBuilder();
        t.Append(c, $"complexity (evidence only, no verdict reads it); settings {Settings}\n");
        t.Append(c, $"  the last training set has {TrainRows} trades, {Events} events ({Wins} wins, {Losses} losses); a logistic model may have {Cap} features\n");
        t.Append(c, $"  effective count of the last training set (overlapping labels counted once): {(EffectiveRows is { } e ? e.ToString("F1", c) : "not computed")} of {TrainRows}\n");
        foreach (var a in Ladder)
        {
            t.Append(c, $"  {a.Scorer,-12} rung {(int)a.Rung} {a.Rung}, {a.FeatureCount} features: {(a.Earned ? "earned" : "not earned - " + string.Join("; ", a.Reasons))}\n");
        }

        foreach (var p in Paired ?? [])
        {
            t.Append(c, $"  paired {p.Scorer} against {p.Below}: {(p.Bootstrap is { } b ? b.ToText(p.Reading) : "not available (" + p.Unavailable + ")")}; folds better {p.Sign.Better} of {p.Sign.Folds}{(p.Sign.Ties > 0 ? $" ({p.Sign.Ties} tied)" : string.Empty)}, sign test p {(p.Sign.PValue is { } v ? v.ToString("F4", c) : "n/a")}\n");
        }

        var alarm = Shifts.Where(s => s.Level == PsiLevel.Alarm).ToArray();
        var watch = Shifts.Where(s => s.Level == PsiLevel.Watch).ToArray();
        var unread = Shifts.Count(s => s.MaxPsi is null);
        t.Append(c, $"  feature shift (population stability index of each fold's test rows against its training rows; the worst of {Folds} folds; bins: up to {PsiBins}, at least {Psi.MinRowsPerBin} rows per side for each bin): {alarm.Length} alarm, {watch.Length} watch, {unread} not computed of {Shifts.Count}");
        AppendShifts(t, "alarm", alarm, featureNames);
        AppendShifts(t, "watch", watch, featureNames);
        t.Append('\n');
        foreach (var s in Stability)
        {
            var unstable = s.Features.Where(f => f.Verdict == StabilityVerdict.Unstable).ToArray();
            var unavailable = s.Features.Count(f => f.Verdict == StabilityVerdict.Unavailable);
            t.Append(c, $"  direction {s.Scorer}: ");
            if (!s.Explained)
            {
                t.Append("not computed (the model could not explain its predictions)\n");
                continue;
            }

            t.Append(c, $"{unstable.Length} of {s.Features.Count} features flip direction in more than the allowed share of the folds (they would be dropped)");
            if (unstable.Length > 0)
            {
                t.Append(": ").Append(List(unstable.Select(f => Label(f.Index, featureNames)), unstable.Length));
            }

            t.Append(c, $"; {unavailable} not judged\n");
        }

        return t.ToString();
    }

    private static void AppendShifts(StringBuilder t, string label, FeatureShift[] shifts, IReadOnlyList<string>? names)
    {
        if (shifts.Length == 0)
        {
            return;
        }

        var items = shifts.OrderByDescending(s => s.MaxPsi).ThenBy(s => s.Index).Select(s => string.Create(CultureInfo.InvariantCulture, $"{Label(s.Index, names)} {s.MaxPsi:F2} (fold {s.Fold})"));
        t.Append(CultureInfo.InvariantCulture, $"; {label}: ").Append(List(items, shifts.Length));
    }

    private static string List(IEnumerable<string> items, int total) =>
        string.Join(", ", items.Take(Listed)) + (total > Listed ? string.Create(CultureInfo.InvariantCulture, $" and {total - Listed} more") : string.Empty);

    private static string Label(int index, IReadOnlyList<string>? names) =>
        names is not null && index >= 0 && index < names.Count ? names[index] : string.Create(CultureInfo.InvariantCulture, $"f{index}");
}
