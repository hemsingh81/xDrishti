namespace XDrishti.Application.Confidence;

/// <summary>The settings that produced the loop figures.</summary>
/// <param name="MinDays">The fewest distinct days.</param>
/// <param name="Draws">Bootstrap draws.</param>
/// <param name="Level">Interval coverage.</param>
/// <param name="HelpsAt">The bar.</param>
/// <param name="MinSlopePer50Rows">The flat-curve limit.</param>
/// <param name="TargetGain">The projection target.</param>
/// <param name="SlopeWindow">The reliability window.</param>
/// <param name="SlopeLow">Lower alarm bar.</param>
/// <param name="SlopeHigh">Upper alarm bar.</param>
/// <param name="SlopeSe">Standard errors from 1.</param>
/// <param name="SettingsHash">The SHA-256 of the settings.</param>
/// <param name="Settings">The text of the settings.</param>
public sealed record LoopSettingsView(int MinDays, int Draws, double Level, double HelpsAt, double MinSlopePer50Rows, double TargetGain, int SlopeWindow, double SlopeLow, double SlopeHigh, double SlopeSe, string SettingsHash, string Settings);

/// <summary>One comparison of two arms.</summary>
/// <param name="Name"><c>retrained_vs_frozen</c> (is retraining helping) or <c>retrained_vs_no_ml</c> (is the model helping).</param>
/// <param name="Verdict"><c>helps</c>, <c>hurts</c>, <c>no_measurable_difference</c>, <c>inconclusive</c> (fewer days than the minimum) or <c>unavailable</c>. "No measurable difference" is an answer.</param>
/// <param name="Candidates">Candidates compared.</param>
/// <param name="Days">Distinct days.</param>
/// <param name="MeanDifference">First arm minus second arm, per candidate.</param>
/// <param name="Low">Lower end of the interval.</param>
/// <param name="High">Upper end.</param>
/// <param name="ProbabilityBetter">P(first arm better).</param>
/// <param name="ProbabilityWorse">P(first arm worse).</param>
/// <param name="Text">The words.</param>
public sealed record LoopArmView(string Name, string Verdict, int Candidates, int Days, double? MeanDifference, double? Low, double? High, double? ProbabilityBetter, double? ProbabilityWorse, string Text);

/// <summary>The three arms on the same candidates.</summary>
/// <param name="State"><c>available</c> or <c>unavailable</c>.</param>
/// <param name="Reason">Why it is unavailable, or null.</param>
/// <param name="Candidates">Candidates in the common set.</param>
/// <param name="FrozenMean">Mean value per candidate of arm A (frozen).</param>
/// <param name="RetrainedMean">Arm B (retrained, the champion now).</param>
/// <param name="NoMlMean">Arm C (no model, take every candidate).</param>
/// <param name="RetrainedVsFrozen">B minus A.</param>
/// <param name="RetrainedVsNoMl">B minus C.</param>
public sealed record LoopArmsView(string State, string? Reason, int Candidates, double? FrozenMean, double? RetrainedMean, double? NoMlMean, LoopArmView? RetrainedVsFrozen, LoopArmView? RetrainedVsNoMl);

/// <summary>The learning curve.</summary>
/// <param name="State"><c>available</c> or <c>unavailable</c>.</param>
/// <param name="Reason">Why it is unavailable, or null.</param>
/// <param name="Verdict"><c>rising</c>, <c>flat</c>, <c>unclear</c> or <c>insufficient</c>; null when unavailable.</param>
/// <param name="PointsUsed">Points fitted.</param>
/// <param name="SlopePer50Rows">Gain per 50 rows.</param>
/// <param name="StandardError">Its standard error per 50 rows, or null with three points.</param>
/// <param name="ProjectedRows">Total rows at which the line reaches the target, or null.</param>
/// <param name="MoreRows">Rows beyond the last point, or null.</param>
/// <param name="Text">The words.</param>
public sealed record LoopCurveView(string State, string? Reason, string? Verdict, int PointsUsed, double? SlopePer50Rows, double? StandardError, double? ProjectedRows, double? MoreRows, string Text);

/// <summary>The reliability slope.</summary>
/// <param name="State"><c>available</c> or <c>unavailable</c>.</param>
/// <param name="Reason">Why it is unavailable, or null.</param>
/// <param name="Verdict"><c>ok</c>, <c>alarm</c>, <c>shown_only</c> (fewer outcomes than the window) or <c>unavailable</c>.</param>
/// <param name="Outcomes">Outcomes in the fit.</param>
/// <param name="Slope">The slope.</param>
/// <param name="Intercept">The intercept.</param>
/// <param name="StandardError">The slope's standard error.</param>
/// <param name="Text">The words.</param>
public sealed record LoopSlopeView(string State, string? Reason, string? Verdict, int Outcomes, double? Slope, double? Intercept, double? StandardError, string Text);

/// <summary>The decision-quality lift.</summary>
/// <param name="State"><c>available</c> or <c>unavailable</c>.</param>
/// <param name="Reason">Why it is unavailable, or null.</param>
/// <param name="TopTrades">Graded trades in the top half.</param>
/// <param name="BottomTrades">Graded trades in the bottom half.</param>
/// <param name="Lift">Top minus bottom mean R.</param>
/// <param name="StandardError">Its standard error.</param>
/// <param name="LiftZ">z of the lift.</param>
/// <param name="ApprovedTickets">Approved tickets that had a stored score.</param>
/// <param name="ApprovedInTop">Of those, in the top half.</param>
/// <param name="ApprovedZ">z of the share against 50 %.</param>
/// <param name="Text">The words.</param>
public sealed record LoopLiftView(string State, string? Reason, int TopTrades, int BottomTrades, double? Lift, double? StandardError, double? LiftZ, int ApprovedTickets, int ApprovedInTop, double? ApprovedZ, string Text);

/// <summary>"Is the learning loop helping?" (spec CF-12 D4 to D7). Report only.</summary>
/// <param name="Settings">The settings.</param>
/// <param name="Arms">The three arms.</param>
/// <param name="Curve">The learning curve.</param>
/// <param name="Slope">The reliability slope.</param>
/// <param name="Lift">The decision-quality lift.</param>
/// <param name="Statements">The honesty sentences.</param>
/// <param name="Line">One line for a report.</param>
public sealed record LoopReportView(LoopSettingsView Settings, LoopArmsView Arms, LoopCurveView Curve, LoopSlopeView Slope, LoopLiftView Lift, IReadOnlyList<string> Statements, string Line);
