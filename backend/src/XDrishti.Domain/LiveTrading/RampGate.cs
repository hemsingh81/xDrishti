namespace XDrishti.Domain.LiveTrading;

/// <summary>The states of an evidence gate (spec P11-04). Only <see cref="Pass"/> lets the step rise; every other word, including a gate that could not be read, blocks.</summary>
public static class GateStates
{
    public const string Pass = "pass";
    public const string Fail = "fail";
    public const string Insufficient = "insufficient";
    public const string Unavailable = "unavailable";
    public const string NotEvaluable = "not_evaluable";
    public const string Info = "info";
}

/// <summary>One gate of the evidence for the next step: its state, the number measured, the threshold and the n it rests on. A number is null whenever the state says it could not be measured, never zero.</summary>
public sealed record RampGate(string Code, string State, double? Number, double? Threshold, int? N, string Reason)
{
    public const string Sessions = "G1_sessions";
    public const string Volume = "G2_volume";
    public const string Slippage = "G3_slippage";
    public const string Discipline = "G4_discipline";
    public const string Containment = "G5_containment";
    public const string Capture = "G6_capture";
    public const string CostDrag = "G7_cost_drag";

    public bool Passes => State == GateStates.Pass;
}
