using XDrishti.Domain.Filings;

namespace XDrishti.Application.Abstractions.Filings;

/// <summary>Optional filters on the accepted entries; they never change the order. A broadcast range leaves out entries without a broadcast instant.</summary>
public sealed record FilingQuery(string? Symbol = null, FilingKind? Kind = null, DateTimeOffset? BroadcastFrom = null, DateTimeOffset? BroadcastTo = null)
{
    public static readonly FilingQuery All = new();
}
