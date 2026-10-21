namespace XDrishti.Simulation.Walking;

/// <summary>The rule a walk applies and the tape it reads (P4-01), and the policy of the partial exits and the time stop (P4-02). <c>Quantity</c> (P4-04) is the full size <c>Q</c> of ADR 0042 as a function of the exact entry and the initial risk, which a pyramid needs to see that an add holds no whole share; null for a gross run, where no add is skipped for its quantity. A walk without a context is the <c>FIXED_RR</c> walk of every run that has no such exit.</summary>
internal sealed record ExitContext(IExitRule? Rule, ExitTape Tape, LegPolicy? Policy = null, Func<decimal, decimal, long>? Quantity = null);
