using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>A dividend of a file with the identity it is stored under.</summary>
internal sealed record IdentifiedDividend(ParsedDividend Row, string ExternalId);

/// <summary>
/// The identity of a dividend of a file (ADR 0028), so that the same payment is never stored twice (unique among the account's dividends that are not voided): <c>d:</c> and 32 hex characters of the hash of the account and the row's content — pay date,
/// normalised name, per share, quantity and amount. Identical rows of one file were counted once by the reader (so the occurrence of a row among identical ones is always the first); two payments that differ in any of these (5 and 4.5 per share of one company
/// on one day) are two identities, and the same file read again gives the same identities. Pure.
/// <para>
/// <b>Do not change what feeds the hash.</b> The identity depends on <see cref="ParsedDividend.ContentKey"/>, on <see cref="DividendNames.Key"/> (its normalisation rules and suffix list) and on the cleaning of names
/// (<c>DividendRules.CleanScripName</c>): changing any of them gives dividends that are stored already new identities, so importing the same file again would store every payment twice. <c>DividendIdentityGoldenTests</c>
/// pins identities and keys of made-up rows and fails on such a change; a deliberate change needs a migration that re-computes the stored identities, and a note in ADR 0028.
/// </para>
/// </summary>
internal static class DividendIdentity
{
    private const int HashLength = 32;

    public static IReadOnlyList<IdentifiedDividend> Identify(Guid accountId, IReadOnlyList<ParsedDividend> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var identified = new List<IdentifiedDividend>(rows.Count);
        foreach (var row in rows)
        {
            var text = string.Create(CultureInfo.InvariantCulture, $"{accountId:N}|{row.ContentKey}");
            identified.Add(new IdentifiedDividend(row, "d:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..HashLength]));
        }

        return identified;
    }
}
