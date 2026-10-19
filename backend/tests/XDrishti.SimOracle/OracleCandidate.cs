namespace XDrishti.SimOracle;

/// <summary>An entry candidate: the decision instant is the end of the candidate's own minute (<c>bars[DecisionBarIndex]</c>).</summary>
public sealed record OracleCandidate(
    OracleSide Side,
    int DecisionBarIndex,
    long DecisionEpochSeconds,
    double TriggerClose,
    double StopLevel);
