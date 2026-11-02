namespace XDrishti.Domain.Filings;

/// <summary>One category of a stored shareholding group. <see cref="Pct"/> is null exactly when the line is unavailable.</summary>
public sealed record ShareholdingLine(string Category, decimal? Pct, decimal? PledgedPct)
{
    public bool IsValue => Pct is not null;
}
