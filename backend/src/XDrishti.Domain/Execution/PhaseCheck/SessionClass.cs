namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>What one trading day of one account is for the measure (spec P12-12 criterion 1). Checked in this order: out of mode, broken, idle, counted.</summary>
public enum SessionClass
{
    OutOfMode,
    Broken,
    Idle,
    Counted,
}
