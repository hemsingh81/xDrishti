namespace XDrishti.Domain.Execution.PhaseCheck;

/// <summary>The three outcomes of the P12 phase check (spec P12-12, ADR 0080 pattern): never a fourth, never MET on thin or unavailable data.</summary>
public static class PhaseVerdicts
{
    public const string Met = "MET";
    public const string NotMet = "NOT MET";
    public const string NotEvaluable = "NOT EVALUABLE";
}
