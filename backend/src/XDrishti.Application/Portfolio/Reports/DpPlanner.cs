using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Income;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>What happens to a line of a depository-charges sheet: stored by an import, or left out because it costs nothing, is on the buy side, was stored before or Dhan's ledger has the charge.</summary>
internal enum DpStatus
{
    New,
    Zero,
    BuySide,
    AlreadyImported,
    AlreadyInLedger,
}

/// <summary>A line with its identity and what happens to it.</summary>
internal sealed record PlannedDp(DpLine Line, string ExternalId, DpStatus Status);

/// <summary>What importing a sheet would do: every line with its status and a digest of all of it.</summary>
internal sealed record DpPlan(IReadOnlyList<PlannedDp> Lines, string Digest)
{
    public int Count(DpStatus status) => Lines.Count(l => l.Status == status);

    public IEnumerable<PlannedDp> ToStore => Lines.Where(l => l.Status == DpStatus.New);

    public decimal AmountToStore => ToStore.Sum(l => l.Line.Charge);
}

/// <summary>
/// Decides what importing a depository-charges sheet does (ADR 0029). Each line gets an identity — a hash of the account, day, ISIN, quantity, side, type and amount and of how many identical lines came before it, so the same sheet again stores nothing and two genuine identical
/// lines are both kept. Then, in this order: a charge of ₹0 is <c>zero</c>; a buy-side line is <c>buy_side</c>; a line whose identity is stored is <c>already_imported</c>; a line whose amount equals a depository charge of Dhan's own ledger on the same day (each ledger charge matches one line; a day on which the ledger has no charge that names itself a depository charge is compared with all its charges) is
/// <c>already_in_ledger</c>; the rest are <c>new</c> and become costs of the account that the net figures count once (<see cref="LedgerCosts"/>). Pure.
/// </summary>
internal static class DpPlanner
{
    /// <summary>The description of a cost entry made from a line; it names a depository charge, which the net figures count as a cost of the account.</summary>
    public const string Description = "DP charges";

    private const int HashLength = 32;

    public static DpPlan Plan(Guid accountId, DpFile file, IReadOnlySet<string> storedIds, IReadOnlyList<LedgerChargeOnDay> brokerCharges, int newNames)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(storedIds);
        ArgumentNullException.ThrowIfNull(brokerCharges);
        var ledger = LedgerSupersession.OpenCharges(brokerCharges);

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var planned = new List<PlannedDp>(file.Lines.Count);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(digest, string.Create(CultureInfo.InvariantCulture, $"v1|{file.BadTotal}|{newNames}"));
        foreach (var line in file.Lines)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{line.Date:yyyy-MM-dd}|{line.Isin}|{line.Quantity}|{line.Side}|{line.Type}|{line.Charge:0.00}");
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            var id = "d:" + Hash(accountId, key, occurrence);
            var status = StatusOf(line, id, storedIds, ledger);
            planned.Add(new PlannedDp(line, id, status));
            Append(digest, $"{id}|{(int)status}");
        }

        return new DpPlan(planned, Convert.ToHexStringLower(digest.GetHashAndReset()));
    }

    /// <summary>The narration of the cost entry of a line: the description and the type of transaction, kept only when the net figures still count the entry as a cost of the account.</summary>
    public static string Narration(DpLine line)
    {
        var text = line.Type.Length == 0 ? Description : $"{Description} - {line.Type}";
        return LedgerCosts.IsAccountLevel(Description, text) ? text : Description;
    }

    public static bool IsOffMarket(DpLine line) => line.Type.Contains("off", StringComparison.OrdinalIgnoreCase) && line.Type.Contains("market", StringComparison.OrdinalIgnoreCase);

    private static DpStatus StatusOf(DpLine line, string id, IReadOnlySet<string> storedIds, Dictionary<(DateOnly Date, decimal Paid), int> ledger)
    {
        if (line.Charge == 0m)
        {
            return DpStatus.Zero;
        }

        if (line.Side == DpSide.Buy)
        {
            return DpStatus.BuySide;
        }

        if (storedIds.Contains(id))
        {
            return DpStatus.AlreadyImported;
        }

        var amount = (line.Date, line.Charge);
        if (ledger.TryGetValue(amount, out var open) && open > 0)
        {
            ledger[amount] = open - 1;
            return DpStatus.AlreadyInLedger;
        }

        return DpStatus.New;
    }

    private static string Hash(Guid accountId, string key, int occurrence)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{accountId:N}|{key}|{occurrence}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..HashLength];
    }

    private static void Append(IncrementalHash hash, string text)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(text));
        hash.AppendData("\n"u8);
    }
}
