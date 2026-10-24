namespace XDrishti.Application.Reporting;

/// <summary>One auto-insight (doc 15 section 6): a fixed-template sentence. <see cref="State"/> is <c>value</c> when the sentence states something; otherwise the text says why it states nothing and carries no figure.</summary>
/// <param name="Kind">The rule: best_strategy, weakest_strategy, basket_gap, side_bias, best_exit, weekday, regime or calibration.</param>
/// <param name="State">value, insufficient, unavailable or not_evaluable.</param>
/// <param name="Text">The sentence.</param>
/// <param name="Cells">The keys of the cells the figures were read from.</param>
public sealed record InsightView(string Kind, string State, string Text, IReadOnlyList<string> Cells);
