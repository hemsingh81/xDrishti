namespace XDrishti.Domain.Reporting;

/// <summary>One member's part of the basket's buy-and-hold return; only exists when the basket return is a nonzero value (BK-06 m15).</summary>
/// <param name="Symbol">The member.</param>
/// <param name="ReturnPct">Its buy-and-hold return in percent.</param>
/// <param name="Share">(R_i / N) / R_B.</param>
public sealed record ContributionRow(string Symbol, double ReturnPct, double Share);
