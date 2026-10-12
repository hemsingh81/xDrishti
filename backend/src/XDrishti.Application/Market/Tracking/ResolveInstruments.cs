using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Tracking;

/// <summary>
/// Looks up a pasted list (up to 5,000 entries) in the master list. An entry is trimmed and compared ignoring case; it can be a canonical symbol
/// (<c>NSE:RELIANCE</c>, <c>NSE:RELIANCE-2026-10-FUT</c>), a ticker (<c>RELIANCE</c> means <c>NSE:RELIANCE</c>) or the ISIN of a share or ETF.
/// An ISIN that names several instruments resolves to the one that is still listed when there is exactly one, otherwise it is <c>ambiguous</c>.
/// Repeated entries and entries naming an instrument already matched are collapsed. Nothing is guessed: what matches nothing is listed as unmatched.
/// </summary>
public sealed record ResolveInstrumentsQuery(ResolveInput Input) : IQuery<Result<ResolveResult>>;

internal sealed class ResolveInstrumentsHandler(IInstrumentQueries queries) : IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>>
{
    public const string NotFound = "not_found";

    public const string Ambiguous = "ambiguous";

    public const string Invalid = "invalid";

    public async ValueTask<Result<ResolveResult>> HandleAsync(ResolveInstrumentsQuery query, CancellationToken cancellationToken)
    {
        var raw = query.Input?.Symbols;
        var entries = raw?.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim()).ToList() ?? [];
        if (entries.Count == 0)
        {
            return Result.Failure<ResolveResult>(MarketErrors.Invalid("symbols", "Paste at least one symbol."));
        }

        if (entries.Count > TrackingLimits.MaxResolveEntries)
        {
            return Result.Failure<ResolveResult>(MarketErrors.Invalid("symbols", $"Use at most {TrackingLimits.MaxResolveEntries} entries per call."));
        }

        var terms = new List<Term>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = 0;
        foreach (var entry in entries)
        {
            var key = entry.ToUpperInvariant();
            if (!seen.Add(key))
            {
                duplicates++;
            }
            else
            {
                terms.Add(new Term(entry, key, IsLookupable(entry)));
            }
        }

        var lookable = terms.Where(t => t.Lookupable).ToList();
        var candidates = lookable.Count == 0
            ? []
            : await queries.FindForResolveAsync(
                [.. lookable.Select(t => t.SymbolKey).Distinct()], [.. lookable.Where(t => t.IsIsin).Select(t => t.Key).Distinct()], cancellationToken);
        var bySymbol = candidates.GroupBy(c => c.Symbol.ToUpperInvariant(), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var byIsin = candidates
            .Where(c => c.Isin is not null && c.Type != InstrumentTypeCodes.Future)
            .GroupBy(c => c.Isin!.ToUpperInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var matched = new List<ResolvedInstrument>();
        var unmatched = new List<UnmatchedEntry>();
        var matchedIds = new HashSet<Guid>();
        foreach (var term in terms)
        {
            var (found, reason) = term.Lookupable ? Find(term, bySymbol, byIsin) : (null, Invalid);
            if (found is null)
            {
                unmatched.Add(new UnmatchedEntry(term.Text, reason!));
            }
            else if (!matchedIds.Add(found.Id))
            {
                duplicates++;
            }
            else
            {
                matched.Add(new ResolvedInstrument(term.Text, found.Id, found.Symbol, found.Name, found.Type, found.Isin, found.Retired, found.Tracked));
            }
        }

        return Result.Success(new ResolveResult(matched, unmatched, entries.Count, duplicates));
    }

    private static (ResolveCandidate? Found, string? Reason) Find(
        Term term, Dictionary<string, ResolveCandidate> bySymbol, Dictionary<string, List<ResolveCandidate>> byIsin)
    {
        if (bySymbol.TryGetValue(term.SymbolKey, out var bySymbolMatch))
        {
            return (bySymbolMatch, null);
        }

        if (term.IsIsin && byIsin.TryGetValue(term.Key, out var withIsin))
        {
            var listed = withIsin.Where(c => !c.Retired).ToList();
            if (withIsin.Count == 1)
            {
                return (withIsin[0], null);
            }

            return listed.Count == 1 ? (listed[0], null) : (null, Ambiguous);
        }

        return (null, NotFound);
    }

    private static bool IsLookupable(string entry) => entry.Length <= TrackingLimits.MaxResolveEntryLength && !entry.Any(char.IsControl);

    private sealed record Term(string Text, string Key, bool Lookupable)
    {
        /// <summary>The canonical symbol the entry names as typed (a ticker gets the exchange prefix).</summary>
        public string SymbolKey { get; } = Key.Contains(':', StringComparison.Ordinal) ? Key : CanonicalSymbol.Exchange + ":" + Key;

        /// <summary>Twelve characters: two letters, nine letters or digits, one digit.</summary>
        public bool IsIsin { get; } = Key.Length == 12 && char.IsAsciiLetter(Key[0]) && char.IsAsciiLetter(Key[1])
            && Key[2..11].All(char.IsAsciiLetterOrDigit) && char.IsAsciiDigit(Key[11]);
    }
}
