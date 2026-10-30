namespace XDrishti.Domain.Execution.Confirm;

/// <summary>
/// What the tap is judged on, read in one go at the moment of the tap (spec P12-08 criteria 4, 8, 10). <see cref="Now"/> is the database clock. A null fact could not be read and never passes: no switch reading is OFF, no mode is not
/// <c>semi_auto</c>, no halt reading is halted, no readiness check is not ready.
/// </summary>
public sealed record ConfirmState(
    DateTimeOffset Now,
    DateTimeOffset ExpiresAt,
    DateOnly TicketSession,
    bool? ExecutionEnabled,
    string? EffectiveMode,
    bool? Halted,
    string? ReadinessVerdict,
    DateOnly? ReadinessSession,
    DateTimeOffset? ReadinessAt,
    bool Unlocked);
