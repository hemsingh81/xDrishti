namespace XDrishti.Execution.Domain;

/// <summary>The execution modes of an account (doc 20 spellings). Only <see cref="SemiAuto"/> and <see cref="AutoLive"/> make the engine call the broker; <see cref="Assisted"/> only prepares text.</summary>
public enum ExecMode
{
    Paper,
    ManualLive,
    Assisted,
    SemiAuto,
    AutoLive,
}

/// <summary>The process environment (a setting of the host, never data): none by default.</summary>
public enum ExecEnvironment
{
    None,
    Sandbox,
    Live,
}

/// <summary>What the mode source answers for an account: its effective mode and the global switch <c>execution_enabled</c> (default OFF).</summary>
public sealed record ModeAnswer(ExecMode EffectiveMode, bool ExecutionEnabled);

public sealed record GateDecision(bool Open, string Reason)
{
    public static readonly GateDecision Allowed = new(true, "open");
}

/// <summary>The account gate: open only when every input says so. No answer, a disabled switch, a non-executing mode or environment none is a refusal with a reason code. "Unavailable" never counts as a pass.</summary>
public static class AccountGate
{
    public static GateDecision MayAct(ModeAnswer? answer, ExecEnvironment environment) => answer switch
    {
        null => new GateDecision(false, "no_mode_answer"),
        { ExecutionEnabled: false } => new GateDecision(false, "execution_disabled"),
        { EffectiveMode: not (ExecMode.SemiAuto or ExecMode.AutoLive) } => new GateDecision(false, "mode_not_executing"),
        _ when environment is not (ExecEnvironment.Sandbox or ExecEnvironment.Live) => new GateDecision(false, "environment_none"),
        _ => GateDecision.Allowed,
    };
}

public static class ProtectionGate
{
    /// <summary>
    /// The gate for a leg that only reduces risk (stop, exit, flatten; P12-04 Authority). The mode is not consulted: an account lowered to paper while a position is open must still be able to protect it. The global switch and the environment still are,
    /// and "no answer" is a refusal. Whether there is a position to protect is the exit policy's question, so the gate alone never admits an entry.
    /// </summary>
    public static GateDecision MayProtect(ModeAnswer? answer, ExecEnvironment environment) => answer switch
    {
        null => new GateDecision(false, "no_mode_answer"),
        { ExecutionEnabled: false } => new GateDecision(false, "execution_disabled"),
        _ when environment is not (ExecEnvironment.Sandbox or ExecEnvironment.Live) => new GateDecision(false, "environment_none"),
        _ => GateDecision.Allowed,
    };
}
