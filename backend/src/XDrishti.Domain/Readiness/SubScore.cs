namespace XDrishti.Domain.Readiness;

/// <summary>One of the six sub-scores of the readiness score (spec CF-11 D2).</summary>
/// <param name="Name">The name (<see cref="EvidenceName"/> and its siblings).</param>
/// <param name="State">Scored, not applicable or unavailable.</param>
/// <param name="Score">0 to 100 when scored, else null.</param>
/// <param name="Reading">The figure it was computed from, or why it could not be.</param>
/// <param name="WhatWouldMoveIt">What would raise it, computed from the anchors (never invented); null when it is already 100 or does not apply.</param>
public sealed record SubScore(string Name, SubScoreState State, double? Score, string Reading, string? WhatWouldMoveIt)
{
    /// <summary>Forward effective n, after costs.</summary>
    public const string EvidenceName = "Evidence";

    /// <summary>Lower bound of the forward mean net R.</summary>
    public const string EffectName = "Effect";

    /// <summary>Power to see 0.3 R.</summary>
    public const string PowerName = "Power at 0.3 R";

    /// <summary>The forward mean against the point-in-time backtest band.</summary>
    public const string AgreementName = "Backtest agreement";

    /// <summary>The calibration error of the model, when there is one.</summary>
    public const string CalibrationName = "Calibration";

    /// <summary>Folds positive and the drift alarm.</summary>
    public const string StabilityName = "Stability";
}
