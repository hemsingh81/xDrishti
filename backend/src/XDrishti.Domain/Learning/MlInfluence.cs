using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>
/// The one influence predicate (CF-10 part 10c-2, spec D6, ADR 0217): <c>MlMayInfluence = HoldOutBeatsNoMl AND ForwardShadowOk AND NoCalibrationAlarm AND SwitchOn</c>. Pure and deterministic; no clock, no I/O. A missing, null or non-finite figure makes its term
/// <see cref="MlTermState.Unavailable"/> and a term that is not <see cref="MlTermState.Holds"/> is false, so an unavailable figure never passes. Every term that does not hold lists its reasons. A model whose hold-out its family already used is refused with
/// <c>holdout_reused</c> whatever the numbers (spec CF-06 D5: a second look is a new trial and cannot make the term true).
/// </summary>
public static class MlInfluence
{
    /// <summary>The term that compares the model with the best no-ML baseline on the locked hold-out.</summary>
    public const string HoldOutBeatsNoMl = nameof(HoldOutBeatsNoMl);

    /// <summary>The term for the forward shadow candidates.</summary>
    public const string ForwardShadowOk = nameof(ForwardShadowOk);

    /// <summary>The term for the calibration detector.</summary>
    public const string NoCalibrationAlarm = nameof(NoCalibrationAlarm);

    /// <summary>The owner's switch.</summary>
    public const string SwitchOn = nameof(SwitchOn);

    /// <summary>The four terms, in the order a result lists them.</summary>
    public static IReadOnlyList<string> TermNames { get; } = [HoldOutBeatsNoMl, ForwardShadowOk, NoCalibrationAlarm, SwitchOn];

    /// <summary>Evaluates the predicate.</summary>
    public static MlInfluenceResult Evaluate(MlInfluenceFacts facts, MlInfluenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(options);
        return new MlInfluenceResult([HoldOut(facts.HoldOut, options), Forward(facts.Forward, options), Calibration(facts.CalibrationAlarm), Switch(facts.SwitchOn)]);
    }

    private static MlTermResult HoldOut(MlHoldOutFacts? h, MlInfluenceOptions o)
    {
        var term = new Term(HoldOutBeatsNoMl);
        if (h is null)
        {
            return term.Unavailable("holdout_not_scored", "The model has not been scored on the locked hold-out.").Done();
        }

        if (h.Reused)
        {
            // whatever the numbers: a look at a spent hold-out cannot make the term true
            return term.Fail("holdout_reused", "The hold-out was already used by this model's family; a second look is a new trial and cannot count. The model needs a fresh hold-out or stays advisory.").Done();
        }

        if (!h.Kept)
        {
            term.Fail("holdout_not_kept", "No hold-out was kept when the model was trained.");
        }

        var c = CultureInfo.InvariantCulture;
        term.Check(h.Rows is { } rows ? rows >= o.HoldOutMinRows : null, "holdout_too_few_rows", string.Create(c, $"The hold-out holds {h.Rows} rows; at least {o.HoldOutMinRows} are needed."), "holdout_rows_unavailable", "The number of hold-out rows is not available.");
        term.Check(Finite(h.ProbabilityBeatsNoMl) is { } p ? p >= o.HoldOutMinProbability : null, "holdout_not_beating_baseline", string.Create(c, $"The probability of beating the best no-ML baseline on the hold-out is {h.ProbabilityBeatsNoMl:F4}; at least {o.HoldOutMinProbability:F2} is needed."), "holdout_probability_unavailable", "The probability of beating the best no-ML baseline on the hold-out is not available.");
        term.Check(Finite(h.PolicyEffectR) is { } e ? e >= 0 : null, "holdout_policy_effect_negative", string.Create(c, $"The effect of the take rule on realised R is {h.PolicyEffectR:F4}; it must not be negative."), "holdout_policy_effect_unavailable", "The effect of the take rule on realised R is not available.");
        return term.Done();
    }

    private static MlTermResult Forward(MlForwardFacts? f, MlInfluenceOptions o)
    {
        var term = new Term(ForwardShadowOk);
        if (f is null)
        {
            return term.Unavailable("forward_not_available", "No forward shadow figures scored by this model are available.").Done();
        }

        var c = CultureInfo.InvariantCulture;
        term.Check(f.GradedCandidates is { } n ? n >= o.ForwardMinCandidates : null, "forward_too_few_candidates", string.Create(c, $"{f.GradedCandidates} graded shadow candidates; at least {o.ForwardMinCandidates} are needed."), "forward_candidates_unavailable", "The number of graded shadow candidates is not available.");
        term.Check(Finite(f.Weeks) is { } w ? w >= o.ForwardMinWeeks : null, "forward_too_short", string.Create(c, $"The forward evidence spans {f.Weeks:F1} weeks; at least {o.ForwardMinWeeks} are needed."), "forward_weeks_unavailable", "The weeks spanned by the forward evidence are not available.");
        term.Check(Finite(f.TopMinusBottomR) is { } s ? s >= 0 : null, "forward_spread_negative", string.Create(c, $"The top half minus the bottom half mean R is {f.TopMinusBottomR:F4}; it must not be negative."), "forward_spread_unavailable", "The top-half minus bottom-half mean R is not available.");
        term.Check(Finite(f.RollingEcePp) is { } g ? g <= o.ForwardMaxEcePp : null, "forward_ece_too_high", string.Create(c, $"The rolling calibration error is {f.RollingEcePp:F2} pp; at most {o.ForwardMaxEcePp:F1} pp is allowed."), "forward_ece_unavailable", "The rolling calibration error is not available.");
        return term.Done();
    }

    private static MlTermResult Calibration(bool? alarm) =>
        alarm switch
        {
            null => new Term(NoCalibrationAlarm).Unavailable("calibration_alarm_unavailable", "The calibration detector has not been read for this model.").Done(),
            true => new Term(NoCalibrationAlarm).Fail("calibration_alarm", "The calibration detector has fired for this model and has not cleared.").Done(),
            false => new Term(NoCalibrationAlarm).Done(),
        };

    private static MlTermResult Switch(bool on) =>
        on ? new Term(SwitchOn).Done() : new Term(SwitchOn).Fail("switch_off", "The owner's switch ml.influence.enabled is off (the default).").Done();

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;

    /// <summary>Collects the reasons of one term; a failure outranks an unavailable figure.</summary>
    private sealed class Term(string name)
    {
        private readonly List<MlInfluenceReason> _reasons = [];
        private bool _failed;

        public Term Fail(string code, string text)
        {
            _failed = true;
            _reasons.Add(new MlInfluenceReason(code, text));
            return this;
        }

        public Term Unavailable(string code, string text)
        {
            _reasons.Add(new MlInfluenceReason(code, text));
            return this;
        }

        /// <summary>Holds when <paramref name="passes"/> is true, fails when false, unavailable when null.</summary>
        public void Check(bool? passes, string failCode, string failText, string missingCode, string missingText)
        {
            switch (passes)
            {
                case true:
                    break;
                case false:
                    Fail(failCode, failText);
                    break;
                default:
                    Unavailable(missingCode, missingText);
                    break;
            }
        }

        public MlTermResult Done() => new(name, _reasons.Count == 0 ? MlTermState.Holds : _failed ? MlTermState.Fails : MlTermState.Unavailable, [.. _reasons]);
    }
}
