using System.Globalization;

namespace XDrishti.Domain.Readiness;

/// <summary>
/// The readiness score (spec CF-11 D2 to D4, part B): six sub-scores between anchors, the minimum as the score, the hard fails that turn any score into 0, and the label. Pure: no clock, no I/O. <b>Unavailable is never a pass</b>: an applicable sub-score with no input is a hard fail,
/// and so is a hard-fail term whose input does not exist; only a term or a sub-score that does not apply (not applicable) is left out, and it is listed.
/// </summary>
public static class ReadinessCalculator
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The names of the hard-fail terms, as they are listed.</summary>
    public const string IntegrityName = "integrity fault";

    /// <summary>The cost-sheet term.</summary>
    public const string CostSheetName = "cost sheet not confirmed or not checked against a bill";

    /// <summary>The CF-09 term.</summary>
    public const string FailureBoundaryName = "CF-09 failure boundary crossed in this epoch";

    /// <summary>The hold-out term.</summary>
    public const string HoldOutName = "hold-out reused for a pass claim";

    /// <summary>The tier term.</summary>
    public const string TierName = "evidence only from tier C";

    /// <summary>The CF-12 term.</summary>
    public const string KillName = "active CF-12 kill-rule demotion";

    /// <summary>Scores a strategy.</summary>
    public static ReadinessScore Compute(ScoreInputs inputs, HardFailInputs faults, ReadinessOptions options)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(faults);
        ArgumentNullException.ThrowIfNull(options);
        var subs = new List<SubScore> { Evidence(inputs), Effect(inputs), PowerSub(inputs), Agreement(inputs), Calibration(inputs), Stability(inputs) };
        var hard = new List<string>();
        var notApplicable = new List<string>();
        Term(faults.Integrity, IntegrityName, hard, notApplicable);
        Term(faults.CostSheet, CostSheetName, hard, notApplicable);
        foreach (var sub in subs)
        {
            switch (sub.State)
            {
                case SubScoreState.Unavailable:
                    hard.Add($"an input of the {sub.Name} sub-score is unavailable ({sub.Reading})");
                    break;
                case SubScoreState.NotApplicable:
                    notApplicable.Add($"{sub.Name} sub-score ({sub.Reading})");
                    break;
                default:
                    break;
            }
        }

        Term(faults.FailureBoundary, FailureBoundaryName, hard, notApplicable);
        Term(faults.HoldOutReused, HoldOutName, hard, notApplicable);
        Term(faults.TierCOnly, TierName, hard, notApplicable);
        Term(faults.KillDemotion, KillName, hard, notApplicable);

        var scored = subs.Where(s => s.State == SubScoreState.Scored).ToList();
        var raw = scored.Count == 0 ? 0 : scored.Min(s => s.Score!.Value);
        var score = hard.Count > 0 ? 0 : raw;
        var weakest = subs.FirstOrDefault(s => s.State == SubScoreState.Unavailable) ?? scored.OrderBy(s => s.Score!.Value).FirstOrDefault();
        return new ReadinessScore(score, raw, ReadinessLabels.Of(score, options), subs, hard, notApplicable, weakest);
    }

    private static void Term(FaultState state, string name, List<string> hard, List<string> notApplicable)
    {
        switch (state)
        {
            case FaultState.Fault:
                hard.Add(name);
                break;
            case FaultState.Unavailable:
                hard.Add($"{name} (the input is unavailable, which is never a pass)");
                break;
            case FaultState.NotApplicable:
                notApplicable.Add(name);
                break;
            default:
                break;
        }
    }

    private static string F(double value, string format = "0.##") => value.ToString(format, Inv);

    private static SubScore Missing(string name, string why) => new(name, SubScoreState.Unavailable, null, why, null);

    private static bool Good(double? value) => value is { } v && double.IsFinite(v);

    private static SubScore Evidence(ScoreInputs i)
    {
        if (i.ForwardNEff is not { } n || !double.IsFinite(n) || n < 0)
        {
            return Missing(SubScore.EvidenceName, "no forward effective n");
        }

        var score = ScoreAnchors.Evidence(n);
        var move = score >= 100 ? null : $"{F(ScoreAnchors.FullNEff - n, "0.#")} more effective trades reach 100";
        return new SubScore(SubScore.EvidenceName, SubScoreState.Scored, score, $"forward n_eff {F(n, "0.#")}", move);
    }

    private static SubScore Effect(ScoreInputs i)
    {
        if (!Good(i.ForwardMeanR) || !Good(i.ForwardSdR) || !Good(i.ForwardNEff) || i.ForwardSdR < 0 || i.ForwardNEff <= 0)
        {
            return Missing(SubScore.EffectName, "no forward mean, spread or effective n");
        }

        var bound = ScoreAnchors.LowerBound(i.ForwardMeanR!.Value, i.ForwardSdR!.Value, i.ForwardNEff!.Value);
        var score = ScoreAnchors.Effect(bound);
        var move = score >= 100 ? null : $"the lower 90 % bound must rise by {F(ScoreAnchors.FullEffectR - bound, "0.###")} R to reach {F(ScoreAnchors.FullEffectR)} R";
        return new SubScore(SubScore.EffectName, SubScoreState.Scored, score, $"lower 90 % bound of the mean net R {F(bound, "0.###")} R (mean {F(i.ForwardMeanR.Value, "0.###")} R)", move);
    }

    private static SubScore PowerSub(ScoreInputs i)
    {
        if (!Good(i.ForwardNEff) || i.ForwardNEff < 0)
        {
            return Missing(SubScore.PowerName, "no forward effective n");
        }

        var n = i.ForwardNEff!.Value;
        var power = ScoreAnchors.Power(n);
        var score = ScoreAnchors.PowerScore(power);
        var needed = ScoreAnchors.NEffForFullPower();
        var move = score >= 100 ? null : $"about {F(Math.Ceiling(needed), "0")} effective trades ({F(Math.Max(0, Math.Ceiling(needed - n)), "0")} more) reach {F(ScoreAnchors.FullPower * 100, "0")} % power";
        return new SubScore(SubScore.PowerName, SubScoreState.Scored, score, $"power {F(power * 100, "0.##")} % to see {F(ScoreAnchors.TargetEffectR)} R at n_eff {F(n, "0.#")}", move);
    }

    private static SubScore Agreement(ScoreInputs i)
    {
        if (i.Band is not { } band)
        {
            return Missing(SubScore.AgreementName, "no point-in-time backtest band, or no forward mean to compare with it");
        }

        if (band == BandPosition.BelowBand)
        {
            return new SubScore(SubScore.AgreementName, SubScoreState.Scored, 0, "the forward mean is below the band's 5th percentile", "the forward mean must come back inside the band");
        }

        var share = i.PositiveMonthShare;
        var full = share is { } s && s >= ScoreAnchors.PositiveMonthShare - 1e-9;
        var reading = share is { } m ? $"inside the band; {F(m * 100, "0")} % of months with {ScoreAnchors.MonthMinTrades} or more trades are positive" : $"inside the band; no month has {ScoreAnchors.MonthMinTrades} or more trades yet";
        return new SubScore(SubScore.AgreementName, SubScoreState.Scored, full ? 100 : 50, reading, full ? null : $"at least {F(ScoreAnchors.PositiveMonthShare * 100, "0")} % of months with {ScoreAnchors.MonthMinTrades} or more trades must be positive");
    }

    private static SubScore Calibration(ScoreInputs i)
    {
        if (!i.ModelApplies)
        {
            return new SubScore(SubScore.CalibrationName, SubScoreState.NotApplicable, null, "no ML model takes part, so there is nothing to calibrate", null);
        }

        if (i.EcePp is not { } ece || !double.IsFinite(ece) || ece < 0)
        {
            return Missing(SubScore.CalibrationName, "no calibration error of the model");
        }

        var score = ScoreAnchors.Calibration(ece);
        var move = score >= 100 ? null : $"the calibration error must fall by {F(ece - ScoreAnchors.FullCalibrationPp, "0.##")} pp to reach {F(ScoreAnchors.FullCalibrationPp)} pp";
        return new SubScore(SubScore.CalibrationName, SubScoreState.Scored, score, $"expected calibration error {F(ece)} pp", move);
    }

    private static SubScore Stability(ScoreInputs i)
    {
        if (i.Folds is not { } folds || i.PositiveFolds is not { } positive || i.DriftAlarm is not { } alarm || folds <= 0 || positive < 0 || positive > folds)
        {
            return Missing(SubScore.StabilityName, "no folds or no drift reading");
        }

        double score;
        if (alarm || positive * 6 <= 3 * folds)
        {
            score = 0;
        }
        else
        {
            score = positive * 6 >= 5 * folds ? 100 : 50;
        }

        var capped = folds < ScoreAnchors.FullFolds && score > 50;
        if (capped)
        {
            score = 50;
        }

        var reading = $"{positive} of {folds} folds positive, drift alarm {(alarm ? "on" : "off")}{(capped ? "; capped at 50 because there are fewer than 6 folds" : string.Empty)}";
        var move = score >= 100 ? null : capped ? "at least 6 folds are needed to go above 50" : alarm ? "the drift alarm must clear" : "at least 5 of 6 folds positive and no drift alarm";
        return new SubScore(SubScore.StabilityName, SubScoreState.Scored, score, reading, move);
    }
}
