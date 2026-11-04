namespace XDrishti.Domain.Options.Structures;

/// <summary>One leg of a paired structure as stored (<c>research.expression_legs</c>): its place in the structure, the option side (<c>CE</c>, <c>PE</c>), the position (<c>long</c>, <c>short</c>), the strike and the entry and exit premiums, in paise.</summary>
public sealed record StructureLegRow(string TradeKey, int Leg, string Side, string Position, int StrikePx, int EntryPremiumPx, int ExitPremiumPx, IReadOnlyList<string> Labels);
