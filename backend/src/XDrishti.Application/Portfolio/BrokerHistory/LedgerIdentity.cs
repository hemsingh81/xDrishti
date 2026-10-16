using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>A ledger line of Dhan with the identity it is stored under.</summary>
internal sealed record IdentifiedLedgerRow(BrokerLedgerRow Row, string ExternalId);

/// <summary>
/// The identity of a ledger entry (ADR 0025): a hash of the account, the voucher date, the voucher number, the description, the signed amount and how many identical lines came before it in the same answer (so two genuine identical
/// vouchers are both kept), 32 hex characters. Reading the same dates again gives the same identities, so nothing is stored twice. Pure.
/// </summary>
internal static class LedgerIdentity
{
    private const int HashLength = 32;

    public static IReadOnlyList<IdentifiedLedgerRow> Identify(Guid accountId, IReadOnlyList<BrokerLedgerRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var identified = new List<IdentifiedLedgerRow>(rows.Count);
        foreach (var row in rows)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{row.Date:yyyy-MM-dd}|{row.VoucherNo}|{row.Description}|{row.Amount:0.00}");
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            identified.Add(new IdentifiedLedgerRow(row, Hash(accountId, key, occurrence)));
        }

        return identified;
    }

    private static string Hash(Guid accountId, string key, int occurrence)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{accountId:N}|{key}|{occurrence}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..HashLength];
    }
}
