namespace XDrishti.Domain.Selection;

/// <summary>A ranked candidate with the portfolio facts the fill needs. A null fact is "not known", never zero or false.</summary>
/// <param name="Ranked">The ranked, eligible candidate (P7-01).</param>
/// <param name="RiskPct">The ticket's risk as a percentage of capital.</param>
/// <param name="Sector">The instrument's sector.</param>
/// <param name="Baskets">Baskets the symbol belongs to; empty when none.</param>
/// <param name="HeldLongTerm">True when the instrument is in the long-term holdings.</param>
public sealed record FillCandidate(RankedCandidate Ranked, double? RiskPct, string? Sector, IReadOnlyList<string>? Baskets, bool? HeldLongTerm)
{
    /// <summary>The instrument.</summary>
    public string Symbol => Ranked.Merged.Winner.Candidate.Symbol;

    /// <summary>The side.</summary>
    public string Side => Ranked.Merged.Winner.Candidate.Side;

    /// <summary>The style of the cell.</summary>
    public string Style => Ranked.Merged.Winner.Candidate.Cell.Style;
}
