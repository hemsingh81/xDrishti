namespace XDrishti.Domain.Market.Baskets.Lists;

/// <summary>A share whose NSE lot differs from the master's (the master stays primary and is never overwritten).</summary>
public sealed record LotMismatch(string Symbol, int Master, int Nse);

/// <summary>The F&amp;O underlyings only the master has and only the NSE file has (no auto-correction of the basket).</summary>
public sealed record FoUniverseDiff(IReadOnlyList<string> OnlyMaster, IReadOnlyList<string> OnlyNse)
{
    public bool Differs => OnlyMaster.Count > 0 || OnlyNse.Count > 0;
}

/// <summary>The cross-checks of the NSE lot file against the master; pure.</summary>
public static class LotChecks
{
    /// <summary>The symbols whose NSE lot differs from the master's lot; a symbol the master has no lot for is not a mismatch.</summary>
    public static IReadOnlyList<LotMismatch> Mismatches(IReadOnlyDictionary<string, int> nse, IReadOnlyDictionary<string, int> master)
    {
        ArgumentNullException.ThrowIfNull(nse);
        ArgumentNullException.ThrowIfNull(master);
        return [.. nse.Where(n => master.TryGetValue(n.Key, out var lot) && lot != n.Value).OrderBy(n => n.Key, StringComparer.Ordinal).Select(n => new LotMismatch(n.Key, master[n.Key], n.Value))];
    }

    public static FoUniverseDiff UniverseDiff(IEnumerable<string> nseSymbols, IEnumerable<string> masterSymbols)
    {
        ArgumentNullException.ThrowIfNull(nseSymbols);
        ArgumentNullException.ThrowIfNull(masterSymbols);
        var nse = nseSymbols.ToHashSet(StringComparer.Ordinal);
        var master = masterSymbols.ToHashSet(StringComparer.Ordinal);
        return new FoUniverseDiff([.. master.Except(nse).Order(StringComparer.Ordinal)], [.. nse.Except(master).Order(StringComparer.Ordinal)]);
    }
}
