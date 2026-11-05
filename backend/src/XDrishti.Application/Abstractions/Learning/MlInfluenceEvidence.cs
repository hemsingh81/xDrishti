using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>What is known about one model beyond the registry and the hold-out ledger (CF-10 part 10c-2). A null part is not available, and the predicate counts that as false.</summary>
/// <param name="HoldOut">The figures of the model's look at the hold-out; used only when the model owns a fresh look.</param>
/// <param name="Forward">The forward shadow figures of candidates scored by this model.</param>
/// <param name="CalibrationAlarm">Whether the P6-09 calibration detector has fired for the model and not cleared.</param>
public sealed record MlInfluenceEvidence(MlHoldOutFigures? HoldOut, MlForwardFacts? Forward, bool? CalibrationAlarm)
{
    /// <summary>Nothing is available.</summary>
    public static MlInfluenceEvidence None { get; } = new(null, null, null);
}
