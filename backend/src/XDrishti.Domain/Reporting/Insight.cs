using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// One auto-insight as a rule-based sentence. <see cref="State"/> is <see cref="MetricState.Value"/> when the sentence states something; otherwise the text says why it does not (a group below the minimum, an input that does not exist) and states no figure.
/// No language model is involved; every number in <see cref="Text"/> is a figure of the cells named in <see cref="Cells"/>.
/// </summary>
/// <param name="Kind">Which rule.</param>
/// <param name="State">Value, or the reason state.</param>
/// <param name="Text">The sentence.</param>
/// <param name="Cells">The keys of the cells the figures were read from.</param>
public sealed record Insight(InsightKind Kind, MetricState State, string Text, ValueList<string> Cells);
