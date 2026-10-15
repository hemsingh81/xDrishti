using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.DividendImport;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>A security a report name was resolved to: its ISIN and ticker (either can be missing), the instrument of the master list and the holding of the account when there are such, and its name. <see cref="Identity"/> is what makes two refs one security.</summary>
internal sealed record SecurityRef(string? Isin, string? Symbol, Guid? InstrumentId, Guid? HoldingId, string Name, bool Retired = false)
{
    public string Identity => Isin ?? "S:" + Symbol;
}

/// <summary>Which rule decided what a report name means, in the order the rules are tried; <c>Choice</c> is the owner's pick in the request.</summary>
internal enum SecurityMatchKind
{
    Choice,
    Alias,
    Symbol,
    Holding,
    Master,
    Learned,
    Partial,
    Ambiguous,
    None,
}

/// <summary>What a name means: one security (<see cref="Security"/>), nothing (<see cref="SecurityMatchKind.None"/>), or — when more than one security fits and nothing is guessed — <see cref="Candidates"/> to choose from.</summary>
internal sealed record SecurityMatch(SecurityMatchKind Kind, SecurityRef? Security, IReadOnlyList<SecurityRef> Candidates)
{
    public static SecurityMatch None { get; } = new(SecurityMatchKind.None, null, []);

    public bool IsResolved => Security is not null;

    public static SecurityMatch Of(SecurityMatchKind kind, SecurityRef security) => new(kind, security, []);

    public static SecurityMatch Between(IReadOnlyList<SecurityRef> candidates) => new(SecurityMatchKind.Ambiguous, null, candidates);
}

/// <summary>
/// Finds what a scrip name of the global transaction report means for one account (ADR 0029), from an in-memory index built once — a file reads nothing per row. The rules are tried in order and the first that answers decides: (1) the owner's choice in the request; (2) a
/// remembered alias (the table of ADR 0028); (3) the ticker in brackets after the name, against the account's holdings and then the instrument master; (4) the name against the account's open holdings, then its closed ones; (5) the instrument master's names; (6) the names that a
/// demat or depository file taught (ISIN-keyed); (7) the same tiers again with a spelling that differs by one word being a prefix of the other (<c>chem</c> and <c>chemicals</c>, <see cref="BillName.Similar"/>). Names are compared as <see cref="DividendNames.Key"/> makes them. Where
/// more than one security fits a tier the answer is ambiguous and lists the candidates: nothing is guessed. Answers are remembered per name. Pure.
/// </summary>
internal sealed class SecurityIndex
{
    /// <summary>The most candidates an ambiguous answer lists.</summary>
    public const int MaxCandidates = 10;

    private readonly Dictionary<Guid, SecurityRef> _holdings = [];
    private readonly Dictionary<Guid, SecurityRef> _shares = [];
    private readonly Dictionary<string, DividendAlias> _aliases;
    private readonly Dictionary<string, SecurityRef> _choices = new(StringComparer.Ordinal);
    private readonly HashSet<string> _held = new(StringComparer.Ordinal);
    private readonly Tier _open = new();
    private readonly Tier _closed = new();
    private readonly Tier _master = new();
    private readonly Tier _learned = new();
    private readonly Dictionary<string, SecurityMatch> _answers = new(StringComparer.Ordinal);

    public SecurityIndex(
        IReadOnlyList<HoldingData> holdings,
        IReadOnlyList<ShareName> shares,
        IReadOnlyList<DividendAlias> aliases,
        IReadOnlyList<SecurityAlias> learned)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(shares);
        ArgumentNullException.ThrowIfNull(aliases);
        ArgumentNullException.ThrowIfNull(learned);
        _aliases = aliases.ToDictionary(a => a.NameKey, StringComparer.Ordinal);
        var byIsin = new Dictionary<string, SecurityRef>(StringComparer.Ordinal);
        foreach (var share in shares.OrderBy(s => s.Retired).ThenBy(s => s.Symbol, StringComparer.Ordinal).ThenBy(s => s.Id))
        {
            var reference = new SecurityRef(TradeRules.NormaliseIsin(share.Isin), NullIfEmpty(TradeRules.NormaliseSymbol(share.Symbol)), share.Id, null, share.Name, share.Retired);
            _shares[share.Id] = reference;
            if (reference.Isin is { } isin)
            {
                byIsin.TryAdd(isin, reference);
            }

            _master.Add(reference, DividendNames.Key(share.Name), DividendNames.Key(share.DisplayName));
            _master.AddSymbol(reference);
        }

        foreach (var holding in holdings.OrderBy(h => h.TradingSymbol, StringComparer.Ordinal).ThenBy(h => h.Id))
        {
            var reference = new SecurityRef(TradeRules.NormaliseIsin(holding.Isin), NullIfEmpty(TradeRules.NormaliseSymbol(holding.TradingSymbol)), holding.InstrumentId, holding.Id, holding.Name);
            _holdings[holding.Id] = reference;
            if (reference.Isin is { } isin)
            {
                _held.Add(isin);
            }

            var tier = holding.IsOpen ? _open : _closed;
            tier.Add(reference, DividendNames.Key(holding.Name), DividendNames.Key(holding.TradingSymbol));
            tier.AddSymbol(reference);
        }

        foreach (var alias in learned.OrderBy(a => a.CreatedAt).ThenBy(a => a.Id))
        {
            var shared = byIsin.GetValueOrDefault(alias.Isin);
            var reference = new SecurityRef(alias.Isin, shared?.Symbol, shared?.InstrumentId, null, alias.Name.Length > 0 ? alias.Name : shared?.Name ?? alias.Isin);
            _learned.Add(reference, alias.NameKey);
        }
    }

    /// <summary>True when the account has (or had) a holding with this ISIN: a row of the BSE is imported only for such a security (ADR 0029).</summary>
    public bool Holds(string? isin) => TradeRules.NormaliseIsin(isin) is { } code && _held.Contains(code);

    /// <summary>One of the account's holdings (open or closed) as a security; null when it is not the account's.</summary>
    public SecurityRef? Holding(Guid id) => _holdings.GetValueOrDefault(id);

    /// <summary>One share of the instrument master as a security; null when the master does not have it.</summary>
    public SecurityRef? Share(Guid id) => _shares.GetValueOrDefault(id);

    /// <summary>A security that only an ISIN says (the owner typed it): the master's share with that ISIN when there is one, else the ISIN alone.</summary>
    public SecurityRef ByIsin(string isin)
    {
        var code = TradeRules.NormaliseIsin(isin)!;
        return _shares.Values.Where(s => s.Isin == code).OrderBy(s => s.Retired).FirstOrDefault() ?? new SecurityRef(code, null, null, null, code);
    }

    /// <summary>Makes the owner's choice count for every name with this key (rule 1). Earlier answers are forgotten.</summary>
    public void Choose(string key, SecurityRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _choices[key] = reference;
        _answers.Clear();
    }

    /// <summary>What the name means (rules 1 to 7).</summary>
    public SecurityMatch Match(BillName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var cacheKey = name.Key + "\u0001" + name.FullKey + "\u0001" + name.SymbolHint;
        if (!_answers.TryGetValue(cacheKey, out var answer))
        {
            _answers[cacheKey] = answer = Decide(name);
        }

        return answer;
    }

    private SecurityMatch Decide(BillName name)
    {
        foreach (var key in new[] { name.Key, name.FullKey }.Distinct())
        {
            if (_choices.TryGetValue(key, out var chosen))
            {
                return SecurityMatch.Of(SecurityMatchKind.Choice, chosen);
            }
        }

        foreach (var key in new[] { name.Key, name.FullKey }.Distinct())
        {
            if (_aliases.TryGetValue(key, out var alias) && ByAlias(alias) is { } remembered)
            {
                return SecurityMatch.Of(SecurityMatchKind.Alias, remembered);
            }
        }

        if (name.SymbolHint is { } hint)
        {
            foreach (var tier in new[] { _open, _closed, _master })
            {
                var found = Pick(tier.BySymbol(hint), SecurityMatchKind.Symbol);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        var tiers = new[] { (_open, SecurityMatchKind.Holding), (_closed, SecurityMatchKind.Holding), (_master, SecurityMatchKind.Master), (_learned, SecurityMatchKind.Learned) };
        foreach (var (tier, kind) in tiers)
        {
            if (Pick(tier.Exact(name.Key), kind) is { } found)
            {
                return found;
            }
        }

        foreach (var (tier, _) in tiers)
        {
            if (Pick(tier.Similar(name.Key), SecurityMatchKind.Partial) is { } found)
            {
                return found;
            }
        }

        return SecurityMatch.None;
    }

    /// <summary>The security a remembered alias points to; an alias that points at nothing (an unallocated dividend) says nothing about a trade.</summary>
    private SecurityRef? ByAlias(DividendAlias alias) => alias.Target switch
    {
        DividendAliasTarget.Holding => _holdings.GetValueOrDefault(alias.HoldingId!.Value),
        DividendAliasTarget.Instrument => _shares.GetValueOrDefault(alias.InstrumentId!.Value),
        _ => null,
    };

    /// <summary>The one security among the candidates (several refs to one security count once; a retired share only when no current one fits); ambiguous when more; null when there are none.</summary>
    private static SecurityMatch? Pick(IReadOnlyList<SecurityRef> candidates, SecurityMatchKind kind)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var distinct = candidates.GroupBy(c => c.Identity, StringComparer.Ordinal).Select(g => g.OrderBy(c => c.Retired).ThenByDescending(c => c.HoldingId is not null).First()).ToList();
        var current = distinct.Where(c => !c.Retired).ToList();
        var pool = current.Count > 0 ? current : distinct;
        return pool.Count == 1 ? SecurityMatch.Of(kind, pool[0]) : SecurityMatch.Between([.. pool.OrderBy(c => c.Symbol, StringComparer.Ordinal).ThenBy(c => c.Isin, StringComparer.Ordinal).Take(MaxCandidates)]);
    }

    private static string? NullIfEmpty(string text) => text.Length == 0 ? null : text;

    /// <summary>The names of one kind of source, found by exact key, by ticker and by a similar spelling.</summary>
    private sealed class Tier
    {
        private readonly Dictionary<string, List<SecurityRef>> _exact = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SecurityRef>> _symbols = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<(string Key, SecurityRef Reference)>> _byFirstWord = new(StringComparer.Ordinal);

        public void Add(SecurityRef reference, params string[] keys)
        {
            foreach (var key in keys.Where(k => k.Length > 0).Distinct(StringComparer.Ordinal))
            {
                Put(_exact, key, reference);
                var first = key.Split(' ')[0];
                if (!_byFirstWord.TryGetValue(first, out var list))
                {
                    _byFirstWord[first] = list = [];
                }

                list.Add((key, reference));
            }
        }

        public void AddSymbol(SecurityRef reference)
        {
            if (reference.Symbol is { } symbol)
            {
                Put(_symbols, symbol, reference);
            }
        }

        public List<SecurityRef> Exact(string key) => _exact.TryGetValue(key, out var found) ? found : [];

        public List<SecurityRef> BySymbol(string symbol) => _symbols.TryGetValue(symbol, out var found) ? found : [];

        public IReadOnlyList<SecurityRef> Similar(string key)
        {
            if (key.Length == 0)
            {
                return [];
            }

            var first = key.Split(' ')[0];
            return _byFirstWord.TryGetValue(first, out var list) ? [.. list.Where(e => BillName.Similar(key, e.Key)).Select(e => e.Reference)] : [];
        }

        private static void Put(Dictionary<string, List<SecurityRef>> index, string key, SecurityRef reference)
        {
            if (!index.TryGetValue(key, out var list))
            {
                index[key] = list = [];
            }

            if (!list.Contains(reference))
            {
                list.Add(reference);
            }
        }
    }
}
