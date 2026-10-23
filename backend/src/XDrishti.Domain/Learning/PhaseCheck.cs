using System.Globalization;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The phase check of P6-18 (ADR 0080): the roadmap exit "meta-model beats baselines OOS; calibration error at most 5 pp; a knob change can be replayed and compared in under 10 minutes", judged from facts that were stored or measured by the caller.
/// A pure function with a hand table. It prints Met only where the data can carry it: a model needs the champion record (Eligible, hold-out kept, at least 6 folds, trials), the calibration needs at least 6 folds, and the timing needs a replay time as well as the comparison time.
/// </summary>
public static class PhaseCheck
{
    /// <summary>The replay-and-compare budget of the roadmap exit, in seconds.</summary>
    public const double ReplayBudgetSeconds = 600;

    /// <summary>The usable folds the roadmap's 2.1 years leave, as stated in the limits.</summary>
    public const string UsableFoldsText = "about 3";

    /// <summary>Judges the three criteria.</summary>
    /// <param name="model">The model judged, or null when none is registered.</param>
    /// <param name="replay">The measured comparison, or null when no experiment is stored.</param>
    /// <param name="maxCalibrationErrorPp">The calibration limit (5).</param>
    /// <param name="replayProblem">Why the stored experiment could not be re-read and judged, when one is stored but the measurement failed; null otherwise.</param>
    public static PhaseCheckReport Evaluate(PhaseModelFacts? model, PhaseReplayFacts? replay, double maxCalibrationErrorPp = 5, string? replayProblem = null)
    {
        var criteria = new List<PhaseCriterion> { Baselines(model), Calibration(model, maxCalibrationErrorPp), Timing(replay, replayProblem) };
        return new PhaseCheckReport(criteria, Limits(model));
    }

    private static PhaseCriterion Baselines(PhaseModelFacts? m)
    {
        const string name = "The meta-model beats the baselines out of sample";
        var c = CultureInfo.InvariantCulture;
        if (m is null)
        {
            return new PhaseCriterion(1, name, PhaseVerdict.NotEvaluable, "no model is registered; nothing to judge (train one with xd learn model).");
        }

        var facts = string.Create(c, $"model {m.Id} ({m.Selection}): stored verdict {m.Verdict}, beat both baselines in {m.FoldsBeatingBoth} of {m.UsedFolds} folds ({m.FoldsNeeded} needed), pooled beats both: {(m.PooledBeatsBoth ? "yes" : "no")}, hold-out {HoldOut(m)}");
        return m.Verdict switch
        {
            ValidationOutcome.CannotBeatBaselines => new PhaseCriterion(1, name, PhaseVerdict.NotMet, facts + "; " + Join(m.Reasons) + "."),
            ValidationOutcome.Inconclusive => new PhaseCriterion(1, name, PhaseVerdict.NotEvaluable, facts + "; inconclusive: " + Join(m.Reasons) + "."),
            _ when m.ChampionReasons.Count > 0 => new PhaseCriterion(1, name, PhaseVerdict.NotEvaluable, facts + "; the verdict is Eligible but the record does not allow a champion: " + Join(m.ChampionReasons) + "."),
            _ => new PhaseCriterion(1, name, PhaseVerdict.Met, facts + "; on folds of data already seen, not on future data."),
        };
    }

    private static PhaseCriterion Calibration(PhaseModelFacts? m, double limitPp)
    {
        const string name = "Calibration error is at most the limit";
        var c = CultureInfo.InvariantCulture;
        if (m is null)
        {
            return new PhaseCriterion(2, name, PhaseVerdict.NotEvaluable, "no model is registered; nothing to judge.");
        }

        if (!double.IsFinite(m.PooledEcePp))
        {
            return new PhaseCriterion(2, name, PhaseVerdict.NotEvaluable, string.Create(c, $"model {m.Id}: the stored pooled calibration error is not a number."));
        }

        var facts = string.Create(c, $"model {m.Id}: pooled out-of-sample calibration error {m.PooledEcePp:F2} pp against {limitPp:F2} pp, over {m.UsedFolds} folds");
        if (m.PooledEcePp > limitPp)
        {
            return new PhaseCriterion(2, name, PhaseVerdict.NotMet, facts + ".");
        }

        return m.UsedFolds < ValidationOptions.DefaultMinFolds
            ? new PhaseCriterion(2, name, PhaseVerdict.NotEvaluable, string.Create(c, $"{facts}: within the limit, but fewer than {ValidationOptions.DefaultMinFolds} folds is too little to call it met."))
            : new PhaseCriterion(2, name, PhaseVerdict.Met, facts + ".");
    }

    private static PhaseCriterion Timing(PhaseReplayFacts? r, string? problem)
    {
        const string name = "A knob change can be replayed and compared in under 10 minutes";
        var c = CultureInfo.InvariantCulture;
        if (r is null && problem is not null)
        {
            return new PhaseCriterion(3, name, PhaseVerdict.NotEvaluable, "an experiment is stored but its two runs could not be read and judged again: " + problem + "; the replay time was not measured (the full Lab replay on 214 instruments is not measured by this check).");
        }

        if (r is null)
        {
            return new PhaseCriterion(3, name, PhaseVerdict.NotEvaluable, "no experiment is stored, so there are no two runs to read and judge; the replay time was not measured (the full Lab replay on 214 instruments is not measured by this check).");
        }

        var compareSeconds = r.MeasuredCompareMs / 1000.0;
        var measured = string.Create(c, $"experiment {r.ExperimentId}: reading its two stored runs ({r.Trades} baseline trades) and judging them took {compareSeconds:F2} s now (stored when recorded: {r.StoredCompareMs / 1000.0:F2} s)");
        if (r.SuppliedReplaySeconds is not { } replaySeconds)
        {
            return compareSeconds >= ReplayBudgetSeconds
                ? new PhaseCriterion(3, name, PhaseVerdict.NotMet, measured + "; the comparison alone is over the 10-minute budget.")
                : new PhaseCriterion(3, name, PhaseVerdict.NotEvaluable, measured + "; the replay that makes the candidate run was not measured (the full Lab replay on 214 instruments is not measured by this check), so the 10-minute criterion cannot be called.");
        }

        var total = replaySeconds + compareSeconds;
        var timed = string.Create(c, $"{measured}; replay {replaySeconds:F1} s as supplied by the owner (not measured or verified here), together {total:F1} s against {ReplayBudgetSeconds:F0} s");
        if (total >= ReplayBudgetSeconds)
        {
            return new PhaseCriterion(3, name, PhaseVerdict.NotMet, timed + ".");
        }

        return string.Equals(r.StoredOutcome, r.MeasuredOutcome, StringComparison.Ordinal)
            ? new PhaseCriterion(3, name, PhaseVerdict.Met, timed + $"; judging again gave the stored outcome ({r.StoredOutcome}).")
            : new PhaseCriterion(3, name, PhaseVerdict.NotEvaluable, timed + $"; judging again gave {r.MeasuredOutcome}, not the stored {r.StoredOutcome}: the comparison was not reproduced.");
    }

    private static List<string> Limits(PhaseModelFacts? m)
    {
        var c = CultureInfo.InvariantCulture;
        return
        [
            "about 2.1 years of history (ISS-19): every result is shaped by one short stretch of the market.",
            string.Create(c, $"quarterly purged folds from 9 months leave {UsableFoldsText} usable folds on that history; {ValidationOptions.DefaultMinFolds} are needed to call a model eligible or a calibration met, so a pass on fewer is never printed as MET."),
            m is null
                ? "no model is registered, so no hold-out can be stated."
                : m.HoldOutStart is { } start
                    ? string.Create(c, $"the model kept a hold-out from {start:yyyy-MM-dd}; this check does not read it and its use at the phase end is the owner's decision (P6-00 question 2).")
                    : "the model kept no hold-out: every number is on folds of data the owner has already looked at (P6-00 question 2).",
            m is null
                ? "training data range unknown (no model)."
                : string.Create(c, $"the model was trained on trades from {m.From:yyyy-MM-dd} to {m.To:yyyy-MM-dd}."),
            "folds are out-of-sample inside data that has been seen, so beating baselines on them is not evidence about future data; fills are modelled (ISS-18).",
            "the full Lab replay on 214 instruments is not measured by this check; only the comparison of two stored runs is.",
            "this check reads stored records only, computes no new statistic and tunes nothing; the model is advisory until P7.",
        ];
    }

    private static string HoldOut(PhaseModelFacts m) => m.HoldOutStart is { } s ? string.Create(CultureInfo.InvariantCulture, $"kept from {s:yyyy-MM-dd}") : "none kept";

    private static string Join(IReadOnlyList<string> reasons) => reasons.Count == 0 ? "no reason stored" : string.Join("; ", reasons);
}
