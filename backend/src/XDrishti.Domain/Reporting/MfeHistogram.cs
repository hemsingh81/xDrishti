using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>Counts of trades by MFE: <see cref="Counts"/> has one bin between each pair of <see cref="Edges"/>; <see cref="AboveCount"/> is the open bin at or above the last edge.</summary>
public sealed record MfeHistogram(ValueList<double> Edges, ValueList<int> Counts, int AboveCount);
