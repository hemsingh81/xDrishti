using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>Who a group is: its coordinates in the order of the dimensions asked for. An empty key is the whole population.</summary>
/// <param name="Parts">The coordinates.</param>
public sealed record GroupKey(ValueList<GroupPart> Parts)
{
    /// <summary><c>Dimension=value</c> parts joined by <c>|</c>; <c>all</c> for the whole population.</summary>
    public string Text => Parts.Count == 0 ? "all" : string.Join('|', Parts.Select(p => $"{p.Dimension}={p.Text}"));

    /// <summary>The values only, joined by <c> / </c>, for a sentence.</summary>
    public string Label => Parts.Count == 0 ? "all trades" : string.Join(" / ", Parts.Select(p => p.Text));

    /// <summary>The value of one dimension, or null when the key has no such coordinate or it is not recorded.</summary>
    public string? ValueOf(ReportDimension dimension) => Parts.FirstOrDefault(p => p.Dimension == dimension)?.Value;
}
