using System.Collections.Immutable;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Learning;

/// <summary>
/// One labelled decision as the validation harness reads it (P6-05, ADR 0067): the label and its horizon from P6-03, the trade date that places it in a fold, the cell that the cell-rate baseline groups by and the features known at the decision.
/// </summary>
/// <param name="Key">The <see cref="SimTrade.Key"/>.</param>
/// <param name="Label">0 or 1 (<see cref="TripleBarrierLabel.Label"/>).</param>
/// <param name="R">The result at resolution, only for the always-take summary; never a model input.</param>
/// <param name="TradeDate">India date of the entry: the date a fold window is cut by.</param>
/// <param name="DecisionEpochSeconds">The instant of the decision.</param>
/// <param name="ResolvedEpochSeconds">Start of the exit minute (the end of the horizon).</param>
/// <param name="CellKey">The cell, as text, for the cell-rate baseline.</param>
/// <param name="Features">The features at the decision (P6-04); may be empty, then the logistic baseline is the base rate.</param>
public sealed record ValidationRow(
    string Key,
    int Label,
    double R,
    DateOnly TradeDate,
    long DecisionEpochSeconds,
    long ResolvedEpochSeconds,
    string CellKey,
    ImmutableArray<double> Features)
{
    /// <summary>A row from a label and the context of its trade.</summary>
    public static ValidationRow From(TripleBarrierLabel label, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(context);
        return new ValidationRow(label.Key, label.Label, label.R, context.TradeDate, label.DecisionEpochSeconds, label.ResolvedEpochSeconds, context.CellKey, context.Features.IsDefault ? [] : context.Features);
    }
}
