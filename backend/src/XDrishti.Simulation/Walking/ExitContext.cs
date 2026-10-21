namespace XDrishti.Simulation.Walking;

/// <summary>The rule a walk applies and the tape it reads (P4-01), and the policy of the partial exits and the time stop (P4-02). A walk without a context is the <c>FIXED_RR</c> walk of every run that has no such exit.</summary>
internal sealed record ExitContext(IExitRule? Rule, ExitTape Tape, LegPolicy? Policy = null);
