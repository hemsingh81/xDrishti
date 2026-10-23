namespace XDrishti.Domain.Selection;

/// <summary>A position already open (swing carried over included), plain data. A null sector or basket list means "not known": limits that need it cannot be checked and block.</summary>
/// <param name="Symbol">The instrument.</param>
/// <param name="Side"><c>BUY</c> or <c>SELL</c>.</param>
/// <param name="Style"><c>intraday</c> or <c>swing</c>.</param>
/// <param name="RiskPct">Open risk as a percentage of capital.</param>
/// <param name="Sector">The sector; null when not known.</param>
/// <param name="Baskets">Baskets the symbol belongs to; null when not known, empty when none.</param>
public sealed record BookPosition(string Symbol, string Side, string Style, double RiskPct, string? Sector = null, IReadOnlyList<string>? Baskets = null);
