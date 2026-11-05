namespace XDrishti.Domain.Learning;

/// <summary>
/// Decision-quality lift (spec CF-12, D7; ADR 0218): how well the model's score sorts the shadow set, and how far the owner's approvals line up with it. Report only; it feeds nothing.
/// </summary>
/// <param name="TopTrades">Graded trades in the top half of their nights.</param>
/// <param name="BottomTrades">Graded trades in the bottom half.</param>
/// <param name="TopMeanR">Mean R of the top half; null when it has no trade.</param>
/// <param name="BottomMeanR">Mean R of the bottom half; null when it has no trade.</param>
/// <param name="Lift">Top minus bottom mean R; null when either half has fewer than two trades or the spread is zero.</param>
/// <param name="StandardError">Standard error of the lift (pooled sd times the square root of 1/n1 + 1/n2); null with the lift.</param>
/// <param name="LiftZ">Lift over its standard error; null with the lift.</param>
/// <param name="ApprovedTickets">Approved tickets whose candidate was in a top or bottom half.</param>
/// <param name="ApprovedInTop">Of those, in the top half.</param>
/// <param name="ApprovedTopShare">The share in the top half; null with no ticket.</param>
/// <param name="ApprovedZ">The share against 50 % by chance, <c>(share - 0.5) / sqrt(0.25 / m)</c>; null with no ticket.</param>
/// <param name="Text">The words, with the numbers, saying when a figure is not significant.</param>
public sealed record LiftResult(int TopTrades, int BottomTrades, double? TopMeanR, double? BottomMeanR, double? Lift, double? StandardError, double? LiftZ, int ApprovedTickets, int ApprovedInTop, double? ApprovedTopShare, double? ApprovedZ, string Text);
