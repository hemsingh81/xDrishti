using System.Globalization;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Cards;

namespace XDrishti.SimExport.Verification;

/// <summary>
/// The seeded stratified sampler of the verification pack (spec P3-09, V1). Pure, on <c>run.Trades</c> only (the <c>OpenAtEnd</c> trades are apart and never drawn), with no random source and no clock: the order of every choice is
/// <see cref="SeededRank"/> over the seed the owner wrote down first. A cell is a strategy, an instrument and the year of the trade date. Each pick takes, among the cells that still hold an unused trade, those whose strategy has the fewest
/// picks, then those whose instrument (inside the strategy) has the fewest, then those whose year (counted over the whole strategy) has the fewest, then those whose cell has the fewest; a tie that remains goes to the lowest rank of the cell.
/// The sizes of the cells and strata are never printed.
/// </summary>
public static class VerificationSampler
{
    /// <summary>The version of the draw; it changes whenever a change of the rules changes a draw.</summary>
    public const string Version = "verify-sampler-1";

    /// <summary>The longest seed.</summary>
    public const int MaxSeedLength = 40;

    /// <summary>The default number of random trades.</summary>
    public const int DefaultCount = 20;

    /// <summary>The most random trades.</summary>
    public const int MaxCount = 100;

    /// <summary>Whether the text is a seed: 1 to 40 characters of <c>A-Z a-z 0-9 . _ -</c>.</summary>
    public static bool IsValidSeed(string? seed) =>
        seed is { Length: >= 1 and <= MaxSeedLength } && seed.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    /// <summary>Draws at most <paramref name="count"/> trades, in draw order; fewer when the pool is smaller.</summary>
    /// <exception cref="ArgumentException">The seed is not valid.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The count is below 0.</exception>
    public static IReadOnlyList<SampledTrade> Draw(IReadOnlyList<SimulationRun> runs, string seed, int count)
    {
        ArgumentNullException.ThrowIfNull(runs);
        if (!IsValidSeed(seed))
        {
            throw new ArgumentException("The seed must be 1 to 40 characters of letters, digits, '.', '_' or '-'.", nameof(seed));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var cells = Cells(runs, seed);
        var strategyPicks = new Dictionary<string, int>(StringComparer.Ordinal);
        var instrumentPicks = new Dictionary<(string, string), int>();
        var yearPicks = new Dictionary<(string, int), int>();
        var picked = new List<SampledTrade>(Math.Min(count, 256));
        while (picked.Count < count)
        {
            var open = cells.Where(c => c.Used < c.Count).ToList();
            if (open.Count == 0)
            {
                break;
            }

            open = KeepMin(open, c => strategyPicks.GetValueOrDefault(c.Strategy));
            open = KeepMin(open, c => instrumentPicks.GetValueOrDefault((c.Strategy, c.Symbol)));
            open = KeepMin(open, c => yearPicks.GetValueOrDefault((c.Strategy, c.Year)));
            open = KeepMin(open, c => c.Used);
            var cell = open.OrderBy(c => c.Rank, StringComparer.Ordinal).ThenBy(c => c.Label, StringComparer.Ordinal).First();

            picked.Add(cell.Take());
            strategyPicks[cell.Strategy] = strategyPicks.GetValueOrDefault(cell.Strategy) + 1;
            instrumentPicks[(cell.Strategy, cell.Symbol)] = instrumentPicks.GetValueOrDefault((cell.Strategy, cell.Symbol)) + 1;
            yearPicks[(cell.Strategy, cell.Year)] = yearPicks.GetValueOrDefault((cell.Strategy, cell.Year)) + 1;
        }

        return picked;
    }

    private static List<T> KeepMin<T>(List<T> items, Func<T, int> measure)
    {
        var min = items.Min(measure);
        return [.. items.Where(i => measure(i) == min)];
    }

    private static List<Cell> Cells(IReadOnlyList<SimulationRun> runs, string seed)
    {
        var cells = new Dictionary<(string, string, int), Cell>();
        for (var r = 0; r < runs.Count; r++)
        {
            foreach (var runTrade in runs[r].Trades)
            {
                var trade = runTrade.Trade;
                var key = (trade.StrategyId, trade.Symbol, trade.TradeDate.Year);
                if (!cells.TryGetValue(key, out var cell))
                {
                    cell = new Cell(trade.StrategyId, trade.Symbol, trade.TradeDate.Year, seed);
                    cells.Add(key, cell);
                }

                cell.Add(trade.Key, new SampledTrade(r, runTrade));
            }
        }

        return [.. cells.Values];
    }

    private sealed class Cell
    {
        private readonly Dictionary<string, SampledTrade> _trades = new(StringComparer.Ordinal);

        private readonly string _seed;

        private IReadOnlyList<string>? _order;

        public Cell(string strategy, string symbol, int year, string seed)
        {
            Strategy = strategy;
            Symbol = symbol;
            Year = year;
            _seed = seed;
            Label = "cell|" + strategy + "|" + symbol + "|" + year.ToString(CultureInfo.InvariantCulture);
            Rank = SeededRank.Of(seed, Label);
        }

        public string Strategy { get; }

        public string Symbol { get; }

        public int Year { get; }

        public string Label { get; }

        public string Rank { get; }

        public int Used { get; private set; }

        public int Count => _trades.Count;

        public void Add(string key, SampledTrade trade) => _trades.TryAdd(key, trade);

        public SampledTrade Take()
        {
            _order ??= SeededRank.Order(_seed + "|trade", _trades.Keys);
            return _trades[_order[Used++]];
        }
    }
}
