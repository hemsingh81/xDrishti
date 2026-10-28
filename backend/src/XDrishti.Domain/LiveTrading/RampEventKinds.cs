namespace XDrishti.Domain.LiveTrading;

/// <summary>The kinds of <c>trading.ramp_events</c> (spec P11-04). The owner writes the first three; the rule <c>rule:ramp</c> writes the others (P11-04b). The table checks who may write which.</summary>
public static class RampEventKinds
{
    public const string SetStep = "set_step";
    public const string ClearHold = "clear_hold";
    public const string ExitConfirmed = "exit_confirmed";
    public const string Hold = "hold";
    public const string StepDown = "step_down";
    public const string AutoStepUp = "auto_step_up";

    /// <summary>The actor of the rule.</summary>
    public const string RuleActor = "rule:ramp";
}
