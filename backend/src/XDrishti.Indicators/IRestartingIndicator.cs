namespace XDrishti.Indicators;

/// <summary>
/// An indicator that starts afresh at the first bar of each India date and that the <see cref="FeatureFrame"/> has to tell when a new date has begun before the indicator itself has seen a bar of it (ADR 0035):
/// <c>vwap</c>, <c>or_high</c>, <c>or_low</c>, <c>today_*</c>, <c>prev_day_*</c> and <c>gap_pct</c>. On a higher timeframe the frame moves to a new date at the first 1-minute bar of it, while the timeframe's own
/// first bar of the date closes later; in between the indicator still holds the previous date's state. It is internal on purpose: the frame is the only caller and the library's public surface does not change.
/// </summary>
internal interface IRestartingIndicator
{
    /// <summary>
    /// A new India date has begun in the frame and this indicator has not been fed a bar of it: report not ready (<see cref="IIndicator.Value"/> is <see cref="double.NaN"/>) until the next
    /// <see cref="IIndicator.Update"/>, which clears it. <see cref="IIndicator.Reset"/> clears it too.
    /// </summary>
    void MarkPending();
}
