using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>
/// The identity of an Action-center item: the rule and the key of the thing it is about (ADR 0022). A holding rule's key is the holding id; the concentration rule's key is the account id and the first 12 hex characters of the SHA-1 of the lower-cased sector
/// name (a sector name can contain any character, so it is not part of the id; it is in the item's numbers). The id <c>{rule}:{key}</c> is therefore safe in a URL path and stable across reads.
/// </summary>
internal readonly record struct ActionKey(string Rule, string Key)
{
    private const int HashHexLength = 12;

    public string Id => Rule + ":" + Key;

    public static ActionKey ForHolding(string rule, Guid holdingId) => new(rule, holdingId.ToString("D", CultureInfo.InvariantCulture));

    public static ActionKey ForSector(Guid accountId, string sector) =>
        new(ActionRuleNames.SectorConcentration, accountId.ToString("D", CultureInfo.InvariantCulture) + ":" + SectorHash(sector));

    /// <summary>The first 12 hex characters of the SHA-1 of the lower-cased sector name (UTF-8): an identity, not a secret.</summary>
    public static string SectorHash(string sector)
    {
        ArgumentNullException.ThrowIfNull(sector);
#pragma warning disable CA5350 // the ADR fixes SHA-1 for the id of a sector: it only tells sectors apart in an id, nothing is protected by it
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(sector.ToLowerInvariant()));
#pragma warning restore CA5350
        return Convert.ToHexStringLower(hash)[..HashHexLength];
    }

    /// <summary>Reads an id from a request; null when it is not the id of any rule's item (unknown rule, a key of the wrong shape). The key is brought to its canonical spelling (lower-case ids).</summary>
    public static ActionKey? Parse(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var colon = id.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return null;
        }

        var rule = id[..colon];
        var key = id[(colon + 1)..];
        if (!ActionRuleNames.All.Contains(rule))
        {
            return null;
        }

        if (ActionRuleNames.IsHoldingRule(rule))
        {
            return Guid.TryParseExact(key, "D", out var holding) ? ForHolding(rule, holding) : null;
        }

        var parts = key.Split(':');
        return parts is [var account, var hash]
            && Guid.TryParseExact(account, "D", out var accountId)
            && hash.Length == HashHexLength
            && hash.All(Uri.IsHexDigit)
            ? new ActionKey(rule, accountId.ToString("D", CultureInfo.InvariantCulture) + ":" + hash.ToLowerInvariant())
            : null;
    }

    /// <summary>The account a concentration item belongs to (its key starts with it); null for any other rule.</summary>
    public Guid? AccountId => Rule == ActionRuleNames.SectorConcentration && Guid.TryParseExact(Key.AsSpan(0, 36), "D", out var account) ? account : null;

    /// <summary>The holding a holding rule's item is about; null for the concentration rule.</summary>
    public Guid? HoldingId => ActionRuleNames.IsHoldingRule(Rule) && Guid.TryParseExact(Key, "D", out var holding) ? holding : null;
}
