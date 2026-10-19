using System.Globalization;
using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Cards;

/// <summary>
/// The file names of the cards (spec P3-04c, C6): <c>cards/&lt;nnnn&gt;-&lt;ticker&gt;-&lt;side&gt;-&lt;date&gt;-&lt;exit&gt;.html</c>. <c>nnnn</c> is the place of the trade in the run's trade order, 1-based, written with
/// <c>max(4, digits of the trade count)</c> digits; the ticker is the symbol without its exchange prefix, every character outside <c>A-Za-z0-9._-</c> replaced by <c>_</c> and cut to 24; side is <c>buy</c> or <c>sell</c>; the date is
/// <c>yyyy-MM-dd</c>; the exit is the export spelling. The number makes every name unique, so two symbols that clean to the same ticker do not collide. The whole name is at most 80 characters.
/// </summary>
public static class CardNames
{
    /// <summary>The folder of the cards in the run folder.</summary>
    public const string Folder = "cards";

    /// <summary>The name of the index page.</summary>
    public const string IndexName = "index.html";

    /// <summary>The longest ticker in a name.</summary>
    public const int MaxTicker = 24;

    /// <summary>The longest whole name.</summary>
    public const int MaxName = 80;

    private const int MinDigits = 4;

    /// <summary>The name of the card of the trade that has place <paramref name="number"/> among <paramref name="tradeCount"/> trades.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The number is below 1 or above the trade count.</exception>
    public static string Of(int number, int tradeCount, SimTrade trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, tradeCount);

        var digits = Math.Max(MinDigits, tradeCount.ToString(CultureInfo.InvariantCulture).Length);
        var name = string.Create(
            CultureInfo.InvariantCulture,
            $"{Folder}/{number.ToString("D" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)}-{Ticker(trade.Symbol)}-{ExportSpellings.Of(trade.Side).ToLowerInvariant()}-{trade.TradeDate:yyyy-MM-dd}-{ExportSpellings.Of(trade.ExitReason)}.html");
        return name.Length <= MaxName ? name : throw new InvalidOperationException("A card name is longer than " + MaxName.ToString(CultureInfo.InvariantCulture) + " characters: " + name.Length.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The symbol without its exchange prefix (up to the first <c>:</c>), cleaned to <c>A-Za-z0-9._-</c> and cut to <see cref="MaxTicker"/>.</summary>
    public static string Ticker(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var colon = symbol.IndexOf(':', StringComparison.Ordinal);
        var bare = colon < 0 ? symbol : symbol[(colon + 1)..];
        var clean = string.Concat(bare.Select(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-' ? c : '_'));
        return clean.Length > MaxTicker ? clean[..MaxTicker] : clean;
    }
}
