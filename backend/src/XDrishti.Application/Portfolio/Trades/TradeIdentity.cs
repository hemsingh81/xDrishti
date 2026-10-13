using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>A trade of a file with the identity it is stored under.</summary>
internal sealed record IdentifiedTrade(ParsedTrade Trade, string ExternalId);

/// <summary>
/// The identity of a trade (ADR 0023), so that the same trade is never stored twice (unique among the account's trades that are not voided). A trade id of the file is the identity together with the trade date (<c>t:yyyyMMdd:</c> + the id — exchanges number trades per day, so an id alone is not unique across days); without one it is a hash of the
/// account and the line's details (date, time, symbol, side, quantity, price, order id) and of how many identical lines came before it in the file (<c>h:</c> + 32 hex characters), so two genuine identical trades are both kept and
/// the same file read again gives the same identities. A manual trade has a random identity and is never deduplicated. Pure.
/// </summary>
internal static class TradeIdentity
{
    private const int HashLength = 32;

    public static IReadOnlyList<IdentifiedTrade> Identify(Guid accountId, IReadOnlyList<ParsedTrade> trades)
    {
        ArgumentNullException.ThrowIfNull(trades);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var identified = new List<IdentifiedTrade>(trades.Count);
        foreach (var trade in trades)
        {
            if (trade.TradeId is { } id)
            {
                identified.Add(new IdentifiedTrade(trade, ForTradeId(trade.Date, id)));
                continue;
            }

            var key = trade.ContentKey;
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            identified.Add(new IdentifiedTrade(trade, "h:" + Hash(accountId, key, occurrence)));
        }

        return identified;
    }

    /// <summary>The identity of a trade that has a trade id: <c>t:yyyyMMdd:id</c>.</summary>
    public static string ForTradeId(DateOnly date, string id) => string.Create(CultureInfo.InvariantCulture, $"t:{date:yyyyMMdd}:{id}");

    /// <summary>The identity of a trade typed in by the owner: random, so no two manual trades are the same trade.</summary>
    public static string ForManual() => "m:" + Guid.NewGuid().ToString("N");

    private static string Hash(Guid accountId, string key, int occurrence)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{accountId:N}|{key}|{occurrence}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..HashLength];
    }
}
