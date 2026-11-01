using System.Text.Json;
using XDrishti.Domain.Market;

namespace XDrishti.Domain.Options;

/// <summary>
/// The configured list of underlyings whose option contracts are kept (setting <c>options.underlyings</c>, ADR 0002 and ADR 0175): canonical symbols of the
/// master list (<c>NSE:NIFTY</c>, <c>NSE:RELIANCE</c>). The stored value is a JSON array of strings; a bare ticker gets the <c>NSE:</c> prefix, case is folded
/// to upper. An entry that is not a symbol is dropped and reported, never a crash; an empty list keeps no contract.
/// </summary>
public sealed class OptionUnderlyings
{
    public const int MaxSymbolLength = 60;

    /// <summary>The four index underlyings of the default list; the 30 shares are added once, at the first import (ADR 0175).</summary>
    public static readonly IReadOnlyList<string> DefaultIndices = ["NSE:NIFTY", "NSE:BANKNIFTY", "NSE:FINNIFTY", "NSE:MIDCPNIFTY"];

    /// <summary>How many F&amp;O shares the default list adds.</summary>
    public const int DefaultShares = 30;

    private readonly HashSet<string> _symbols;
    private readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> _tickers;

    private OptionUnderlyings(IEnumerable<string> symbols, IReadOnlyList<string> rejected)
    {
        _symbols = new HashSet<string>(symbols, StringComparer.Ordinal);
        Symbols = [.. _symbols.Order(StringComparer.Ordinal)];
        Rejected = rejected;

        // The bare ticker of every symbol, and its index spelling with spaces, for the cheap look at an unsplit line.
        var tickers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var symbol in _symbols)
        {
            var ticker = symbol[(CanonicalSymbol.Exchange.Length + 1)..];
            tickers.Add(ticker);
            tickers.Add(ticker.Replace('_', ' '));
        }

        _tickers = tickers.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public static OptionUnderlyings Empty { get; } = new([], []);

    /// <summary>The listed canonical symbols, ordered.</summary>
    public IReadOnlyList<string> Symbols { get; }

    /// <summary>Entries of the setting that are not a symbol (dropped).</summary>
    public IReadOnlyList<string> Rejected { get; }

    public bool IsEmpty => _symbols.Count == 0;

    public static OptionUnderlyings From(IEnumerable<string?> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var kept = new List<string>();
        var rejected = new List<string>();
        foreach (var entry in entries)
        {
            var symbol = Normalise(entry);
            if (symbol is null)
            {
                rejected.Add(entry ?? "null");
            }
            else
            {
                kept.Add(symbol);
            }
        }

        return new OptionUnderlyings(kept, rejected);
    }

    /// <summary>Reads the stored value (a JSON array of strings); a value that is not one is the empty list with the whole value rejected.</summary>
    public static OptionUnderlyings Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(stored);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new OptionUnderlyings([], ["(not a list)"]);
            }

            return From(document.RootElement.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null));
        }
        catch (JsonException)
        {
            return new OptionUnderlyings([], ["(not a list)"]);
        }
    }

    /// <summary>The value to store: a JSON array of the symbols, ordered.</summary>
    public string ToStored() => JsonSerializer.Serialize(Symbols);

    public bool Contains(string canonicalSymbol) => _symbols.Contains(canonicalSymbol);

    /// <summary>A cheap look at a raw <c>UNDERLYING_SYMBOL</c>: false means no listed underlying can have it.</summary>
    public bool MightList(ReadOnlySpan<char> underlyingTicker) => _tickers.Contains(underlyingTicker);

    private static string? Normalise(string? entry)
    {
        var text = entry?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var prefix = CanonicalSymbol.Exchange + ":";
        var ticker = text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : text;
        var valid = ticker.Length > 0 && prefix.Length + ticker.Length <= MaxSymbolLength && ticker.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '&');
        return valid ? prefix + ticker : null;
    }
}
