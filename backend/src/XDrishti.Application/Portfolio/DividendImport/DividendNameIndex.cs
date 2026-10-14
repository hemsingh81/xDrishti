using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// Finds what a dividend's scrip name means for one account (ADR 0028), from an in-memory index of the account's holdings, the instrument master's names and the account's remembered aliases — built once, so a file of 20,000 rows reads nothing per row. The
/// rules are tried in order and the first that answers decides: (1) a remembered alias; (2) the ISIN or symbol column of the file, against the open holdings; (3) the name's key equal to the key of an open holding's name (or symbol); (4) the name's key
/// inside an open holding's name or the other way round, as whole words (<see cref="DividendNames.Contains"/>); (5) the instrument master — a unique share by ISIN, symbol or name key; (6) nothing. Where more than one holding fits (rules 2 to 4) nothing is
/// guessed: the answer lists the candidates. Answers are remembered per name, ISIN and symbol. Pure.
/// </summary>
internal sealed class DividendNameIndex
{
    /// <summary>The most candidates an ambiguous answer lists.</summary>
    public const int MaxCandidates = 20;

    private readonly Dictionary<Guid, HoldingData> _holdings;
    private readonly Dictionary<Guid, ShareName> _shares = [];
    private readonly Dictionary<string, DividendAlias> _aliases;
    private readonly Dictionary<string, List<HoldingData>> _openByIsin = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<HoldingData>> _openBySymbol = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<HoldingData>> _openByName = new(StringComparer.Ordinal);
    private readonly List<(string Key, HoldingData Holding)> _openNames = [];
    private readonly Dictionary<string, List<ShareName>> _sharesByIsin = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ShareName>> _sharesBySymbol = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ShareName>> _sharesByName = new(StringComparer.Ordinal);
    private readonly Dictionary<(string, string?, string?), DividendMatch> _answers = [];

    public DividendNameIndex(IReadOnlyList<HoldingData> holdings, IReadOnlyList<ShareName> shares, IReadOnlyList<DividendAlias> aliases)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(aliases);
        _holdings = holdings.ToDictionary(h => h.Id);
        _aliases = aliases.ToDictionary(a => a.NameKey, StringComparer.Ordinal);
        foreach (var holding in holdings.Where(h => h.IsOpen).OrderBy(h => h.TradingSymbol, StringComparer.Ordinal).ThenBy(h => h.Id))
        {
            if (TradeRules.NormaliseIsin(holding.Isin) is { } isin)
            {
                Add(_openByIsin, isin, holding);
            }

            if (TradeRules.NormaliseSymbol(holding.TradingSymbol) is { Length: > 0 } symbol)
            {
                Add(_openBySymbol, symbol, holding);
            }

            var nameKey = DividendNames.Key(holding.Name);
            if (nameKey.Length > 0)
            {
                Add(_openByName, nameKey, holding);
                _openNames.Add((nameKey, holding));
            }

            var symbolKey = DividendNames.Key(holding.TradingSymbol);
            if (symbolKey.Length > 0 && symbolKey != nameKey)
            {
                Add(_openByName, symbolKey, holding);
            }
        }

        foreach (var share in shares)
        {
            _shares[share.Id] = share;
            if (TradeRules.NormaliseIsin(share.Isin) is { } isin)
            {
                Add(_sharesByIsin, isin, share);
            }

            if (TradeRules.NormaliseSymbol(share.Symbol) is { Length: > 0 } symbol)
            {
                Add(_sharesBySymbol, symbol, share);
            }

            var nameKey = DividendNames.Key(share.Name);
            var displayKey = DividendNames.Key(share.DisplayName);
            if (nameKey.Length > 0)
            {
                Add(_sharesByName, nameKey, share);
            }

            if (displayKey.Length > 0 && displayKey != nameKey)
            {
                Add(_sharesByName, displayKey, share);
            }
        }
    }

    /// <summary>One of the account's holdings (open or closed) by id; null when it is not the account's.</summary>
    public HoldingData? Holding(Guid id) => _holdings.GetValueOrDefault(id);

    /// <summary>One share of the master list by id; null when the master does not have it.</summary>
    public ShareName? Share(Guid id) => _shares.GetValueOrDefault(id);

    /// <summary>What the row's scrip name means (rules 1 to 6).</summary>
    public DividendMatch Match(ParsedDividend row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var cacheKey = (row.NameKey, row.Isin, row.Symbol);
        if (!_answers.TryGetValue(cacheKey, out var answer))
        {
            _answers[cacheKey] = answer = Decide(row);
        }

        return answer;
    }

    private DividendMatch Decide(ParsedDividend row)
    {
        if (_aliases.TryGetValue(row.NameKey, out var alias) && ByAlias(alias) is { } remembered)
        {
            return remembered;
        }

        if (row.Isin is { } isin && Lookup(_openByIsin, isin, DividendMatchKind.Isin) is { } byIsin)
        {
            return byIsin;
        }

        if (row.Symbol is { } symbol && Lookup(_openBySymbol, symbol, DividendMatchKind.Symbol) is { } bySymbol)
        {
            return bySymbol;
        }

        if (Lookup(_openByName, row.NameKey, DividendMatchKind.Name) is { } byName)
        {
            return byName;
        }

        var fitting = _openNames.Where(n => DividendNames.Contains(n.Key, row.NameKey)).Select(n => n.Holding).DistinctBy(h => h.Id).ToList();
        if (fitting.Count == 1)
        {
            return DividendMatch.Of(DividendMatchKind.Contains, fitting[0]);
        }

        if (fitting.Count > 1)
        {
            return DividendMatch.Between(DividendMatchKind.Contains, Listed(fitting));
        }

        var share = (row.Isin is { } code ? Unique(_sharesByIsin, code) : null)
            ?? (row.Symbol is { } ticker ? Unique(_sharesBySymbol, ticker) : null)
            ?? Unique(_sharesByName, row.NameKey);
        return share is null ? DividendMatch.None : DividendMatch.Of(DividendMatchKind.Master, share);
    }

    private DividendMatch? ByAlias(DividendAlias alias) => alias.Target switch
    {
        DividendAliasTarget.Holding when _holdings.TryGetValue(alias.HoldingId!.Value, out var holding) => DividendMatch.Of(DividendMatchKind.Alias, holding),
        DividendAliasTarget.Instrument when _shares.TryGetValue(alias.InstrumentId!.Value, out var share) => DividendMatch.Of(DividendMatchKind.Alias, share),
        DividendAliasTarget.None => new DividendMatch(DividendMatchKind.Alias, null, null, []),
        _ => null,
    };

    private static DividendMatch? Lookup(Dictionary<string, List<HoldingData>> index, string key, DividendMatchKind kind)
    {
        if (!index.TryGetValue(key, out var found))
        {
            return null;
        }

        return found.Count == 1 ? DividendMatch.Of(kind, found[0]) : DividendMatch.Between(kind, Listed(found));
    }

    private static List<HoldingData> Listed(List<HoldingData> holdings) =>
        [.. holdings.OrderBy(h => h.TradingSymbol, StringComparer.Ordinal).ThenBy(h => h.Id).Take(MaxCandidates)];

    /// <summary>The one share a key leads to: the only one, or the only one the master has not retired; null when there are none or still more than one.</summary>
    private static ShareName? Unique(Dictionary<string, List<ShareName>> index, string key)
    {
        if (!index.TryGetValue(key, out var found))
        {
            return null;
        }

        var current = found.DistinctBy(s => s.Id).Where(s => !s.Retired).ToList();
        if (current.Count > 0)
        {
            return current.Count == 1 ? current[0] : null;
        }

        var retired = found.DistinctBy(s => s.Id).ToList();
        return retired.Count == 1 ? retired[0] : null;
    }

    private static void Add<T>(Dictionary<string, List<T>> index, string key, T value)
    {
        if (!index.TryGetValue(key, out var list))
        {
            index[key] = list = [];
        }

        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }
}
