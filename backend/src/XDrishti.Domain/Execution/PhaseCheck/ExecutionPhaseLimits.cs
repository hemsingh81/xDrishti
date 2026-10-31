namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>The honest limits of the P12 phase check, printed in every outcome (spec P12-12, Risks).</summary>
public static class ExecutionPhaseLimits
{
    public static readonly IReadOnlyList<string> Limits =
    [
        "Calendar time: thirty counted sessions need at least thirty trading days with a human-confirmed live order and a clean day; the plan produces no ticket on many days, so the real time may be several months.",
        "Zero breaks proves our records agree with the broker's data, not with the exchange's, and not that the strategy makes money.",
        "The count depends on the reconciliation's break definitions and its freeze behaviour; foreign orders typed by hand in the same account can create breaks.",
        "Human-confirmed is read from a database row that the API writes after the session and password checks; a compromised API process could forge it.",
        "Sandbox sessions are excluded and so are canary sessions; a session with a broker read that failed is broken, never clean.",
        "Stored records only: the continuous runs are a view at a resolution of the reconciliation interval, so a break that appears and disappears between two runs is not seen; actions at the broker are not recorded unless the reconciliation sees their effect.",
        "A working order and a position carried over several days are read from the stored legs and fills; the read covers at most 200 sessions and says when it stops there.",
        "Protection coverage, the highest sends per second, refused double submits and per-fill slippage have no stored source in this build and read unavailable; the canary dates C1 and C2 are not recorded anywhere and read not recorded.",
        "Nothing is written and no test is run: look-ahead, determinism and the state machine are proved by tests, not by this check.",
    ];
}
