using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>
/// The dividends the owner typed in, looked up to tell whether a row of a file may be a second entry of one of them (ADR 0028): the same holding, pay date and rupees. A row with a holding is compared by that alone. A row without one (unallocated, or
/// with an instrument only) is also compared by the scrip - the instrument, the ISIN or the ticker, or the row's normalised name against the ticker - so that unrelated scrips paying the same rupees on the same day are not taken for each other.
/// A typed-in unallocated dividend that says nothing about its scrip (no instrument, ISIN or ticker) may be any scrip, so it is compared by date and rupees alone: a false alarm costs one tick, a missed duplicate costs a wrong figure.
/// </summary>
internal sealed class DividendManualIndex
{
    private readonly Dictionary<(Guid? Holding, DateOnly Date, decimal Amount), List<StoredDividendKey>> _byKey = [];

    public DividendManualIndex(IEnumerable<StoredDividendKey> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        foreach (var key in stored)
        {
            if (!_byKey.TryGetValue((key.HoldingId, key.PayDate, key.Amount), out var list))
            {
                _byKey[(key.HoldingId, key.PayDate, key.Amount)] = list = [];
            }

            list.Add(key);
        }
    }

    /// <summary>True when a typed-in dividend of the same holding (or unallocated, of the same scrip), date and rupees exists.</summary>
    public bool Has(ParsedDividend row, DividendMatch match)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(match);
        if (!_byKey.TryGetValue((match.HoldingId, row.PayDate, row.Amount), out var candidates))
        {
            return false;
        }

        return match.HoldingId is not null || candidates.Any(c => !SaysScrip(c) || SameScrip(c, row, match));
    }

    private static bool SaysScrip(StoredDividendKey key) => key.InstrumentId is not null || !string.IsNullOrWhiteSpace(key.Isin) || !string.IsNullOrWhiteSpace(key.Symbol);

    private static bool SameScrip(StoredDividendKey stored, ParsedDividend row, DividendMatch match)
    {
        if (stored.InstrumentId is { } instrument && match.InstrumentId == instrument)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(stored.Isin) && (Same(stored.Isin, row.Isin) || Same(stored.Isin, match.Share?.Isin)))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(stored.Symbol))
        {
            return false;
        }

        return Same(stored.Symbol, row.Symbol) || Same(stored.Symbol, match.Share?.Symbol) || DividendNames.Key(stored.Symbol) == row.NameKey;
    }

    private static bool Same(string? first, string? second) => first is not null && second is not null && string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
}
