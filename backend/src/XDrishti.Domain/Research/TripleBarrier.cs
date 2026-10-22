using XDrishti.Domain.Common;

namespace XDrishti.Domain.Research;

/// <summary>
/// The triple-barrier labeller (P6-03, ADR 0065, doc 09 §1 layer 3 and §4): a pure function of one finished <see cref="SimTrade"/>. The upper barrier is the target, the lower the stop and the vertical one the time limit (square-off or longest
/// hold); the label is 1 only when the target ended the trade. The walker has already decided which barrier came first, so the labeller reads no price: it cannot see the future beyond the barrier definition. No clock, no random.
/// </summary>
public static class TripleBarrier
{
    /// <summary>The version of the rule; changes whenever a label can change (a training set records it).</summary>
    public const string RuleVersion = "tb-1";

    /// <summary>The basis a run reports: net when the trade was charged, filled when its fills were modelled, else exact (doc 09 §9: grade on what could be executed).</summary>
    /// <param name="trade">The trade.</param>
    public static LabelBasis BasisOf(SimTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        return trade.Costing is not null ? LabelBasis.Net : trade.Fills == FillBasis.Modelled ? LabelBasis.Filled : LabelBasis.Exact;
    }

    /// <summary>Labels a trade on the basis of its run.</summary>
    /// <param name="trade">The trade.</param>
    public static Result<TripleBarrierLabel> Label(SimTrade trade) => Label(trade, BasisOf(trade));

    /// <summary>Labels a trade, reporting R on the given basis.</summary>
    /// <param name="trade">The trade.</param>
    /// <param name="basis">Which result to report.</param>
    public static Result<TripleBarrierLabel> Label(SimTrade trade, LabelBasis basis)
    {
        ArgumentNullException.ThrowIfNull(trade);
        if (trade.TargetPrice is null)
        {
            return Result.Failure<TripleBarrierLabel>(Error.Validation("label.no_target", "The trade has no target level, so there is no upper barrier."));
        }

        BarrierOutcome outcome;
        switch (trade.ExitReason)
        {
            case ExitReason.Target:
                outcome = BarrierOutcome.Target;
                break;
            case ExitReason.Stop:
                outcome = BarrierOutcome.Stop;
                break;
            case ExitReason.SquareOff:
                outcome = BarrierOutcome.SessionEnd;
                break;
            case ExitReason.MaxHold:
                outcome = BarrierOutcome.MaxHold;
                break;
            case ExitReason.EmaExit:
            case ExitReason.TimeStop:
                outcome = BarrierOutcome.RuleExit;
                break;
            default:
                return Result.Failure<TripleBarrierLabel>(Error.Validation("label.unresolved", "The bars ended while the position was open, so no barrier was reached."));
        }

        double r;
        switch (basis)
        {
            case LabelBasis.Exact:
                r = trade.RExact;
                break;
            case LabelBasis.Filled:
                r = trade.RFilled;
                break;
            case LabelBasis.Net:
                if (trade.Costing is null)
                {
                    return Result.Failure<TripleBarrierLabel>(Error.Validation("label.no_net_r", "The trade was not charged, so it has no net result."));
                }

                r = trade.Costing.RNet;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unknown label basis.");
        }

        var minutes = Math.Max(0, (trade.ExitEpochSeconds - trade.EntryEpochSeconds) / 60);
        return Result.Success(new TripleBarrierLabel(
            trade.Key, outcome == BarrierOutcome.Target ? 1 : 0, outcome, basis, r, minutes, trade.SessionsHeld, trade.DecisionEpochSeconds, trade.ExitEpochSeconds, trade.Legs is { Count: > 0 }));
    }
}
