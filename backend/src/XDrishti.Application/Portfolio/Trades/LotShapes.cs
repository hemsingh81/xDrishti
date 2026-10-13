using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>
/// The shape of a set of lots as text, and its digest (ADR 0023): date, quantity, unit cost, source, default flag and, for a closed lot, close day, close price and the estimated flag — in sorted order, so the same lots always have the
/// same shape whatever order they are stored in. Used to see whether a rebuild would change anything, and as the digest the preview returns and an import with rebuild must echo. Pure.
/// </summary>
internal static class LotShapes
{
    public static string Of(IEnumerable<HoldingLot> lots)
    {
        ArgumentNullException.ThrowIfNull(lots);
        return Join(lots.Select(l => Line(l.AcquiredOn, l.Qty, l.UnitCost, l.Source, l.UsesDefaultDate, l.ClosedOn, l.ClosePrice, l.Estimated)));
    }

    /// <summary>The shape of planned lots: closed pieces are never estimated.</summary>
    public static string Of(IEnumerable<LotPlan> plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Join(plan.Select(p => Line(p.AcquiredOn, p.Qty, p.UnitCost, p.Source, p.UsesDefaultDate, p.ClosedOn, p.ClosePrice, estimated: false)));
    }

    /// <summary>Lower-case hex SHA-256 of the lines (each followed by a line feed).</summary>
    public static string Digest(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Concat(lines.Select(l => l + "\n")))));
    }

    private static string Join(IEnumerable<string> lines) => string.Join('\n', lines.Order(StringComparer.Ordinal));

    private static string Line(DateOnly acquired, long qty, decimal unitCost, LotSource source, bool usesDefaultDate, DateOnly? closedOn, decimal? closePrice, bool estimated) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{acquired:yyyy-MM-dd}|{qty}|{unitCost:0.0000}|{source}|{usesDefaultDate}|{closedOn:yyyy-MM-dd}|{closePrice:0.0000}|{estimated}");
}
