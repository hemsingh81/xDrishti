using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Running;

/// <summary>
/// One open position per instrument and side (spec P3-03d, ADR 0040, D11), for the candidates of one side in decision order. It sits between the scanner and the walks, so the candidates depend on no exit and the same candidates can be
/// walked under any exit policy. Let <c>E</c> be the <i>hold end</i> of the last accepted trade (<see cref="WalkOutcome.HoldEndEpochSeconds"/>: its exit instant, or the end of its due session for a trade that left at the last close
/// because its due minute was missing): a candidate whose decision instant <c>D</c> is at or before <c>E</c> is <b>blocked</b>, and so is every candidate after an accepted trade that ended with the data (the position was still open
/// at every later decision of the list, so a short run and a longer one block the same candidates). A blocked candidate is not walked and becomes a <see cref="NoTradeReason.PositionOpen"/>; the gate comes before the walk, so a
/// candidate that would have been a <c>LimitNotReached</c> is a <c>PositionOpen</c> all the same. The gate models the strategy's position, not the account's: it knows no quantity and no cost.
/// </summary>
internal sealed class PositionGate
{
    private bool _any;

    private bool _endedWithTheData;

    private long _holdEnd;

    /// <summary>The decision at <paramref name="decisionEpochSeconds"/> is taken while a position is open.</summary>
    public bool Blocks(long decisionEpochSeconds) => _endedWithTheData || (_any && decisionEpochSeconds <= _holdEnd);

    /// <summary>Records an accepted outcome; a no-trade is not a position.</summary>
    public void Accept(in WalkOutcome outcome)
    {
        if (outcome.Trade is not { } trade)
        {
            return;
        }

        _any = true;
        _endedWithTheData = trade.ExitReason == ExitReason.EndOfData;
        _holdEnd = outcome.HoldEndEpochSeconds;
    }

    /// <summary>The outcome of a candidate the gate did not let through: no walk, no minute read.</summary>
    public static WalkOutcome BlockedOutcome() => WalkResults.NoTrade(NoTradeReason.PositionOpen, 0, 0).Value;

    /// <summary>
    /// Walks the candidates in order with <paramref name="walk"/> and applies the gate between two walks: a blocked candidate is not walked. A failure of a walk is returned as it is.
    /// </summary>
    public static Result<IReadOnlyList<CandidateOutcome>> Apply(IEnumerable<EntryCandidate> candidates, Func<EntryCandidate, Result<WalkOutcome>> walk)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(walk);
        var gate = new PositionGate();
        var outcomes = new List<CandidateOutcome>();
        foreach (var candidate in candidates)
        {
            if (gate.Blocks(candidate.DecisionEpochSeconds))
            {
                outcomes.Add(new CandidateOutcome(candidate, BlockedOutcome()));
                continue;
            }

            var walked = walk(candidate);
            if (walked.IsFailure)
            {
                return Result.Failure<IReadOnlyList<CandidateOutcome>>(walked.Error);
            }

            gate.Accept(walked.Value);
            outcomes.Add(new CandidateOutcome(candidate, walked.Value));
        }

        return Result.Success<IReadOnlyList<CandidateOutcome>>(outcomes);
    }
}
