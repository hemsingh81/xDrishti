using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.Options.Evaluation;

/// <summary>
/// Protocol R of P14-10 (the iron condor of the range family, <c>expr:iron_condor</c>): the condor has no cash twin, so it is judged on its own out-of-sample R: KEEP needs the evaluable quarters, the condors, the Lab's gates through <c>VerdictEvaluator</c> with N, both block-bootstrap lower bounds of
/// the mean R at or above <c>0.05 R x Factor(N)</c>, a positive mean in two thirds of the quarters and in the latest epoch, net costs and a fallback share of 5 % or less; DROP is the same minima, not KEEP, net and the larger upper bound under 0.05 R; a missing quantity is NOT_EVALUABLE and named.
/// It is protocol O's core with the condor's words and options (<see cref="ExpressionStudyOptions.Condor"/>) and the cash R of every pair taken as 0, so the paired difference is the condor's own R. Every condor in the range family before the hold-out is out of sample: nothing is fitted.
/// </summary>
public static class ProtocolR
{
    public const string Protocol = ExpressionFamilies.CondorProtocol;

    /// <summary>The development verdict over the condors dated before the hold-out.</summary>
    public static Result<ProtocolOResult> Evaluate(ProtocolOInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return ProtocolO.Evaluate(input with { Wording = ProtocolWording.Condor });
    }

    /// <summary>The one reading of the hold-out.</summary>
    public static Result<ProtocolOutcome> HoldOut(IReadOnlyList<ExpressionPair> pairs, ExpressionStudyOptions options, DateOnly holdOutStart) => ProtocolO.HoldOut(pairs, options, holdOutStart, ProtocolWording.Condor);
}
