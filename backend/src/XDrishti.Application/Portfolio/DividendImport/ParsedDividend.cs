using System.Globalization;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// One valid dividend row of a file (ADR 0028). <see cref="Line"/> is the line of the file the row starts on. <see cref="Name"/> is the scrip name as the file wrote it (plain text, cleaned); <see cref="NameKey"/> is what names are compared by
/// (<see cref="DividendNames.Key"/>). <see cref="Isin"/> and <see cref="Symbol"/> are the file's columns when it has them. <see cref="PerShare"/> and <see cref="Quantity"/> are null when the file has none; <see cref="Amount"/> is the rupees paid.
/// </summary>
internal sealed record ParsedDividend(int Line, DateOnly PayDate, string Name, string NameKey, string? Isin, string? Symbol, decimal? PerShare, long? Quantity, decimal Amount)
{
    /// <summary>What makes two rows the same payment: everything but the line and the exact spelling of the name.</summary>
    public string ContentKey => string.Create(
        CultureInfo.InvariantCulture,
        $"{PayDate:yyyy-MM-dd}|{NameKey}|{(PerShare is { } share ? share.ToString("0.0000", CultureInfo.InvariantCulture) : "-")}|{(Quantity is { } qty ? qty.ToString(CultureInfo.InvariantCulture) : "-")}|{Amount:0.00}");
}
