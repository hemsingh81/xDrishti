namespace XDrishti.Domain.Learning;

/// <summary>Everything the influence predicate reads about one model (CF-10 part 10c-2). A part that is null is not available.</summary>
/// <param name="HoldOut">The hold-out figures, or null when none could be had.</param>
/// <param name="Forward">The forward shadow figures, or null when none could be had.</param>
/// <param name="CalibrationAlarm">Whether the P6-09 calibration detector has fired for the model and not cleared; null when it could not be read.</param>
/// <param name="SwitchOn">The owner's switch <c>ml.influence.enabled</c>; false by default and when it could not be read.</param>
public sealed record MlInfluenceFacts(MlHoldOutFacts? HoldOut, MlForwardFacts? Forward, bool? CalibrationAlarm, bool SwitchOn);
