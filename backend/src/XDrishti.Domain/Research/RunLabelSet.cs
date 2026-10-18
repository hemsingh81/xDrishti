namespace XDrishti.Domain.Research;

/// <summary>
/// What a P3 run says it is (spec P3-04b1, E5): the labels that are always shown, in a fixed order, and the list of what the fills do not model. Built by <see cref="RunLabels.Of"/>, printed by the banner, written into
/// <c>run.json</c> and the readme. It holds text only: no HTML, no figure about performance.
/// </summary>
/// <param name="Lines">The labels, in the order of <see cref="RunLabels.Of"/>.</param>
/// <param name="NotModelled">What the fill model does not do, as <see cref="FillLimits.NotModelled"/> lists it.</param>
public sealed record RunLabelSet(IReadOnlyList<string> Lines, IReadOnlyList<FillLimit> NotModelled);
