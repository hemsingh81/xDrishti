namespace XDrishti.Domain.Readiness;

/// <summary>
/// The inputs of the six sub-scores (spec CF-11 D2), all from the forward (paper edge) series except where noted. A null is an input that does not exist: the sub-score reads unavailable, which is a hard fail. The calibration sub-score applies only with a model
/// (<see cref="ModelApplies"/>).
/// </summary>
public sealed record ScoreInputs
{
    /// <summary>Evidence: the forward effective n after costs (CF-08, CF-01).</summary>
    public double? ForwardNEff { get; init; }

    /// <summary>Effect and power: the forward mean net R.</summary>
    public double? ForwardMeanR { get; init; }

    /// <summary>Effect: the standard deviation of a forward trade's R.</summary>
    public double? ForwardSdR { get; init; }

    /// <summary>Backtest agreement: the forward mean against the P10-04 band.</summary>
    public BandPosition? Band { get; init; }

    /// <summary>Backtest agreement: the share of months with at least 5 trades that are positive; null when no month qualifies.</summary>
    public double? PositiveMonthShare { get; init; }

    /// <summary>Calibration: whether a model takes part at all (no model, no calibration sub-score).</summary>
    public bool ModelApplies { get; init; }

    /// <summary>Calibration: the expected calibration error, in percentage points (CF-10).</summary>
    public double? EcePp { get; init; }

    /// <summary>Stability: the number of folds (P6-05).</summary>
    public int? Folds { get; init; }

    /// <summary>Stability: how many of them are positive.</summary>
    public int? PositiveFolds { get; init; }

    /// <summary>Stability: whether the drift alarm (P6-09) is on.</summary>
    public bool? DriftAlarm { get; init; }
}
