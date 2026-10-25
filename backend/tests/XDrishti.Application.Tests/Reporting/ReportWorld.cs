using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Tests.Reporting;

/// <summary>A report world for the handler tests: stored runs and trades in memory behind the ports, the owner's stored confirmation as a field, and the loader built over them. The fake source applies the same filters a store does.</summary>
internal sealed class ReportWorld : IReportTradeSource, IChargeSheetConfirmationSource, IHoldingClassifications
{
    /// <summary>The sector of each instrument in the master.</summary>
    public Dictionary<Guid, string> Sectors { get; } = [];

    public ValueTask<IReadOnlyDictionary<Guid, string>> SectorsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, string>>(Sectors);

    public ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>> BasketsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<BasketRef>>>(new Dictionary<Guid, IReadOnlyList<BasketRef>>());

    public static readonly DateOnly Day0 = new(2026, 1, 5);

    public static readonly DateTimeOffset T0 = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public List<ReportRunInfo> Runs { get; } = [];

    public List<ReportTrade> Trades { get; } = [];

    public ChargeSheetConfirmation? Confirmation { get; set; }

    /// <summary>How many times each port was asked, so a test can show a refusal was decided without reading trades.</summary>
    public int TradeStreams { get; private set; }

    /// <summary>The one gate of the world: loaders made by <see cref="Loader"/> share it, as the scoped loaders of the host share the singleton.</summary>
    public ReportGate Gate { get; } = new();

    public ReportLoader Loader => new(this, this, this, Gate, new FakeTimeProvider(T0), NullLogger<ReportLoader>.Instance);

    /// <summary>A stand-in for the hash of the rates of a sheet: stable per name, 64 lower-case hex characters.</summary>
    public static string Rates(string sheet) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sheet)));

    /// <summary>The owner's confirmation of a sheet with its rates.</summary>
    public static ChargeSheetConfirmation Confirm(string sheet, string? rates = null) => new(sheet, rates ?? Rates(sheet), new DateOnly(2026, 10, 1));

    public static string Key(char c) => new(c, 64);

    public ReportRunInfo AddRun(char key, string strategy = "ORB", string universe = "basket:b1", string? sheet = null, string? style = "intraday", string? exit = "FIXED_RR{rr=2}", int createdDays = 0, int startOffset = 0, int endOffset = 300, string? rates = null)
    {
        var run = new ReportRunInfo(Key(key), strategy, "basket", "members as of today", universe, Day0.AddDays(startOffset), Day0.AddDays(endOffset), sheet, style, style is null ? null : "stored strategy", exit, exit is null ? null : "stored comparison abc", 0, 2, T0.AddDays(createdDays), sheet is null ? null : rates ?? Rates(sheet));
        Runs.Add(run);
        return run;
    }

    /// <summary>Adds one trade per R value to a run, on consecutive days; the trade key follows the strategy, the run and the index so that keys differ between runs unless <paramref name="sameKeys"/>.</summary>
    public void AddTrades(char key, IEnumerable<double> rs, string strategy = "ORB", string timeframe = "15m", string side = "BUY", string[]? baskets = null, bool sameKeys = false, double? cost = null, int firstDay = 0, string symbol = "AAA")
    {
        var i = 0;
        foreach (var r in rs)
        {
            var tradeKey = sameKeys ? $"{strategy}|{symbol}|{side}|{i:D4}" : $"{strategy}|{symbol}|{side}|{key}{i:D4}";
            Trades.Add(new ReportTrade(Key(key), tradeKey, strategy, timeframe, side, Guid.Empty, symbol, new ValueList<string>(baskets ?? []), Day0.AddDays(firstDay + i), Day0.AddDays(firstDay + i), r, cost is null ? null : r - cost, 1));
            i++;
        }

        var index = Runs.FindIndex(x => x.RunKey == Key(key));
        Runs[index] = Runs[index] with { ClosedTrades = Trades.Count(t => t.RunKey == Key(key)) };
    }

    public ValueTask<IReadOnlyList<ReportRunInfo>> ListRunsAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ReportRunInfo>>([.. Runs.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.RunKey, StringComparer.Ordinal)]);

    public async IAsyncEnumerable<ReportTrade> StreamTradesAsync(IReadOnlyList<string> runKeys, ReportTradeFilter filter, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TradeStreams++;
        await Task.Yield();
        foreach (var trade in Trades)
        {
            if (runKeys.Contains(trade.RunKey)
                && (filter.Strategies.Count == 0 || filter.Strategies.Contains(trade.Strategy))
                && (filter.Timeframes.Count == 0 || filter.Timeframes.Contains(trade.Timeframe))
                && (filter.Sides.Count == 0 || filter.Sides.Contains(trade.Side))
                && (filter.Symbols.Count == 0 || filter.Symbols.Contains(trade.Symbol))
                && (filter.Baskets.Count == 0 || trade.BasketIds.Any(b => filter.Baskets.Contains(Guid.Parse(b))))
                && (filter.From is null || trade.EntryDate >= filter.From)
                && (filter.To is null || trade.EntryDate <= filter.To))
            {
                yield return trade;
            }
        }
    }

    public ValueTask<ChargeSheetConfirmation?> FindAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Confirmation);

    public static ReportQuery Query(ReportQueryInput? input = null)
    {
        var parsed = ReportQuery.Parse(input ?? new ReportQueryInput());
        parsed.IsSuccess.ShouldBeTrue(parsed.IsFailure ? parsed.Error.Message : string.Empty);
        return parsed.Value;
    }
}
