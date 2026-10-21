namespace XDrishti.Simulation.Walking;

/// <summary>
/// The ladder of <c>PYRAMID</c> as decimals (P4-04, D1): the share of the full size entered first, the progress levels (in initial risks) at whose bar close an add is attempted, the share each add holds and how many of the levels
/// may be used. The ATR trail of the combined position is the walk's <see cref="IExitRule"/>, not part of the plan.
/// </summary>
/// <param name="InitialShare">The share of the full size <c>Q</c> the first fill holds (<c>initial_pct / 100</c>).</param>
/// <param name="Levels">The progress levels, strictly increasing.</param>
/// <param name="Shares">The share of <c>Q</c> of each add, as long as <paramref name="Levels"/>.</param>
/// <param name="MaxAdds">The most levels used, from the first.</param>
internal sealed record PyramidPlan(decimal InitialShare, IReadOnlyList<decimal> Levels, IReadOnlyList<decimal> Shares, int MaxAdds);
