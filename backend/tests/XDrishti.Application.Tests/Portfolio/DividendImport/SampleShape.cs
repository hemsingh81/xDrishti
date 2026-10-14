using System.Globalization;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Tests.Portfolio.DividendImport;

/// <summary>One payment of a made-up dividend file.</summary>
internal sealed record Payment(DateOnly Date, int Name, string Text, decimal PerShare, long Quantity)
{
    public decimal Paid => PerShare * Quantity;
}

/// <summary>
/// A made-up dividend file with the shape of the owner's (nothing of it is the owner's data): 254 rows of 106 names — 62 dated 2024, 100 dated 2025, 92 dated 2026 — in three blocks, one row repeated identically, one name paid twice on one day with different
/// per-share amounts, September written <c>Sept</c>, amounts of a thousand or more grouped with commas, names written with <c>Ltd</c>, <c>Limited</c>, <c>&amp;</c> and plain. The first <see cref="Held"/> names are the account's holdings (written differently
/// in the holdings: upper case, <c>AND</c>, <c>INDUSTRIES LIMITED</c>); of the others the first <see cref="InMaster"/> are in the instrument master and the rest nowhere. The facts are worked out from the model that wrote the file, not by reading it.
/// </summary>
internal sealed class SampleShape
{
    public const int Names = 106;

    public const int Held = 71;

    public const int InMaster = 10;

    private static readonly string[] First = ["Orion", "Delta", "Lumen", "Kestrel", "Vertex", "Aurora", "Zephyr", "Nimbus", "Quartz", "Helios", "Cobalt", "Pioneer", "Summit", "Falcon"];

    private static readonly string[] Second = ["Steel", "Power", "Foods", "Chemicals", "Bank", "Motors", "Textiles", "Pharma", "Energy", "Cement", "Logistics", "Paper"];

    private static readonly decimal[] PerShares = [0.5m, 1m, 1.5m, 2m, 2.25m, 3m, 4.5m, 5m, 7.5m, 10m, 14m, 20m, 27.5m];

    private static readonly long[] Quantities = [1, 2, 3, 5, 8, 10, 15, 20, 25, 40, 60, 80, 100, 150, 250];

    public SampleShape()
    {
        var payments = new List<Payment>();
        for (var k = 0; k < 253; k++)
        {
            var name = k % Names;
            var year = k < 62 ? 2024 : k < 162 ? 2025 : 2026;
            var start = year == 2024 ? new DateOnly(2024, 6, 13) : new DateOnly(year, 1, 2);
            var span = year == 2024 ? 200 : year == 2025 ? 360 : 285;
            payments.Add(new Payment(start.AddDays(k * 7 % span), name, Spelling(name, k), PerShares[k % PerShares.Length], Quantities[(k * 3) % Quantities.Length]));
        }

        // One name paid twice on one day with different per-share amounts (two genuine payments).
        var day = payments[70].Date;
        payments[70] = payments[70] with { PerShare = 4.5m, Quantity = 100 };
        payments[71] = payments[71] with { Date = day, Name = payments[70].Name, Text = Spelling(payments[70].Name, 71), PerShare = 5m, Quantity = 100 };

        // One row written twice in a row (identical).
        Repeated = payments[200];
        Distinct = payments;
        var rows = new List<Payment>(payments);
        rows.Insert(201, Repeated);
        Rows = rows;
    }

    /// <summary>The 253 different payments.</summary>
    public IReadOnlyList<Payment> Distinct { get; }

    /// <summary>All 254 rows in file order (the repeated one twice).</summary>
    public IReadOnlyList<Payment> Rows { get; }

    public Payment Repeated { get; }

    public decimal TotalPaid => Rows.Sum(p => p.Paid);

    public decimal DistinctPaid => Distinct.Sum(p => p.Paid);

    public int Before2025 => Distinct.Count(p => p.Date.Year < 2025);

    public static bool IsHeld(int name) => name < Held;

    public static bool IsInMaster(int name) => name >= Held && name < Held + InMaster;

    public int DistinctOf(Func<int, bool> names) => Distinct.Count(p => names(p.Name));

    public decimal DistinctPaidOf(Func<int, bool> names) => Distinct.Where(p => names(p.Name)).Sum(p => p.Paid);

    public string Csv() => DividendFiles.Csv([.. Rows.Select(p => $"{DividendFiles.DhanDate(p.Date)},{p.Text},{p.PerShare.ToString(CultureInfo.InvariantCulture)},{p.Quantity},{DividendFiles.Money(p.Paid)}")]);

    /// <summary>The company as the file names it: plain, with <c>Ltd</c>, with <c>Limited</c>, or in capitals; every tenth has an ampersand.</summary>
    public static string Base(int name) => name % 10 == 3
        ? $"{First[name % First.Length]} {Second[(name / First.Length * 5 + name) % Second.Length]} & Engineering"
        : $"{First[name % First.Length]} {Second[(name / First.Length * 5 + name) % Second.Length]}";

    private static string Spelling(int name, int k) => (k % 4) switch
    {
        0 => Base(name),
        1 => Base(name) + " Ltd",
        2 => Base(name) + " Limited",
        _ => Base(name).ToUpperInvariant(),
    };

    /// <summary>The name of a holding as the broker writes it.</summary>
    public static string HoldingName(int name) => Base(name).ToUpperInvariant().Replace("&", "AND", StringComparison.Ordinal) + (name % 2 == 0 ? " INDUSTRIES LIMITED" : " LTD.");

    public static string Symbol(int name) => (First[name % First.Length] + Second[(name / First.Length * 5 + name) % Second.Length]).ToUpperInvariant() + name.ToString(CultureInfo.InvariantCulture);

    public static ShareName Share(int name) => new(Guid.CreateVersion7(), Symbol(name), "INE" + (100000 + name).ToString(CultureInfo.InvariantCulture) + "A01", Base(name).ToUpperInvariant() + " LTD", Base(name), false);
}
