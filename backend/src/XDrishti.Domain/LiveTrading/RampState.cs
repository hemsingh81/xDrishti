namespace XDrishti.Domain.LiveTrading;

/// <summary>Where an account stands on the ramp: the newest ramp event decides (spec P11-04). No event means no step and no live account.</summary>
/// <param name="EventId">The newest ramp event; allocations carry it.</param>
/// <param name="Step">1 to 3.</param>
/// <param name="Hold">Whether raising is blocked (the evaluator of P11-04b writes it; the owner clears it).</param>
/// <param name="ExitConfirmed">Whether the owner has recorded <c>exit_confirmed</c> (S3 needs it; the table refuses the step without it).</param>
public sealed record RampState(long EventId, int Step, bool Hold, bool ExitConfirmed = false);
