namespace XDrishti.Simulation.Walking;

/// <summary>The rule a walk applies and the tape it reads (P4-01). A walk without one is the <c>FIXED_RR</c> walk of every production run.</summary>
internal sealed record ExitContext(IExitRule Rule, ExitTape Tape);
