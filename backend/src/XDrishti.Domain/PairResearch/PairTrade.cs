namespace XDrishti.Domain.PairResearch;

/// <summary>One simulated pair trade (P14-13): four fills, the gross rupees, the net rupees (null when no owner's cost sheet was used or a leg had no rates: gross, labelled), the rupee risk and R. Prices are rupees on the tick grid.</summary>
/// <param name="PairKey">The stored key, <c>Y|X</c>.</param>
/// <param name="Side">Long or short spread.</param>
/// <param name="EntryEpochSeconds">The later of the two entry fills.</param>
/// <param name="ExitEpochSeconds">The later of the two exit fills.</param>
/// <param name="ZEntry">The z at the decision that opened the pair.</param>
/// <param name="ZExit">The z at the decision that closed it, or null when the square-off closed it with no z.</param>
/// <param name="ExitReason"><c>exit</c>, <c>stop</c>, <c>square_off</c> or <c>end_of_data</c>.</param>
/// <param name="QtyY">Y shares.</param>
/// <param name="QtyX">X shares.</param>
/// <param name="GrossInr">The rupees before costs.</param>
/// <param name="NetInr">The rupees after the four legs' charges, or null.</param>
/// <param name="RiskInr">The rupee risk.</param>
/// <param name="R">Net (else gross) rupees over the risk.</param>
/// <param name="TurnoverInr">The sum of the four fills' turnover.</param>
/// <param name="EntryY">The filled price of the Y leg at entry.</param>
/// <param name="ExitY">The filled price of the Y leg at exit.</param>
/// <param name="EntryX">The filled price of the X leg at entry.</param>
/// <param name="ExitX">The filled price of the X leg at exit.</param>
/// <param name="Labels">The labels that travel with the trade (<c>gross</c>, <c>leg_late</c>, <c>costs_unavailable</c>).</param>
public sealed record PairTrade(
    string PairKey, PairSide Side, long EntryEpochSeconds, long ExitEpochSeconds, double ZEntry, double? ZExit, string ExitReason, int QtyY, int QtyX, decimal GrossInr, decimal? NetInr, decimal RiskInr, double R, decimal TurnoverInr, decimal EntryY, decimal ExitY, decimal EntryX, decimal ExitX, IReadOnlyList<string> Labels);
