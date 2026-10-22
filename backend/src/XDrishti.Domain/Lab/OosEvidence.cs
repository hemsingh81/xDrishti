namespace XDrishti.Domain.Lab;

/// <summary>The out-of-sample numbers a verdict is judged on (spec P5-03); <paramref name="Method"/> is <c>walk_forward</c> or something weaker.</summary>
public sealed record OosEvidence(int OosTrades, double ExpectancyR, double ProfitFactor, int Folds, int PositiveFolds, double MaxDrawdownR, double CalibrationErrorPp, string Method)
{
    /// <summary>The only method whose numbers can PASS.</summary>
    public const string WalkForward = "walk_forward";
}
