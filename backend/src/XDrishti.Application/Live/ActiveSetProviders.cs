using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Live;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Live;

/// <summary>A source of the active set (ADR 0024 §2): open positions and armed tickets plug in later as more providers. Scoped: it reads through ports that are.</summary>
internal interface IActiveSetProvider
{
    ValueTask<IReadOnlyList<ActiveSetCandidate>> ListAsync(CancellationToken cancellationToken);
}

/// <summary>The open holdings that map to an NSE instrument of the master list, in quote mode, valued by what is held (the order when the set must be cut). Holdings that are not on the NSE, retired or unmapped are not proposed.</summary>
internal sealed class HoldingsProvider(ILivePrices holdings) : IActiveSetProvider
{
    public async ValueTask<IReadOnlyList<ActiveSetCandidate>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await holdings.ListHeldSecuritiesAsync(cancellationToken))
            .Select(h => new ActiveSetCandidate(h.InstrumentId, FeedSegments.NseEquity, h.SecurityId, FeedMode.Quote, ActiveSetReason.Holding, h.Value))];
}

/// <summary>The instruments of the active price alerts that can be quoted (the master list has an NSE equity id for them), in ticker mode; an alert on an instrument with no such id is not proposed and stays active for the 5-minute check.</summary>
internal sealed class AlertsProvider(IPriceAlerts alerts) : IActiveSetProvider
{
    public async ValueTask<IReadOnlyList<ActiveSetCandidate>> ListAsync(CancellationToken cancellationToken) =>
        [.. (await alerts.ListActiveAsync(cancellationToken))
            .Where(a => !string.IsNullOrWhiteSpace(a.SecurityId))
            .Select(a => new ActiveSetCandidate(a.InstrumentId, FeedSegments.NseEquity, a.SecurityId!, FeedMode.Ticker, ActiveSetReason.Alert))];
}

/// <summary>
/// The indices: the benchmark of the portfolio and the context indices NIFTY 50, NIFTY BANK and INDIA VIX, in ticker mode, when the master list has them in the index segment. One that is missing is skipped with one log line
/// (once per process, so a master list without INDIA VIX does not log every minute).
/// </summary>
internal sealed class IndicesProvider(IIndexSecurities indices, IPortfolioBenchmark benchmark, MissingIndexReport reported, ILogger<IndicesProvider> logger) : IActiveSetProvider
{
    /// <summary>The context indices, by the canonical symbol and display name the Dhan master gives them.</summary>
    public static readonly IReadOnlyList<IndexName> ContextIndices =
    [
        new("NIFTY 50", "NSE:NIFTY", "Nifty 50"),
        new("NIFTY BANK", "NSE:BANKNIFTY", "Nifty Bank"),
        new("INDIA VIX", "NSE:INDIAVIX", "India VIX"),
    ];

    public async ValueTask<IReadOnlyList<ActiveSetCandidate>> ListAsync(CancellationToken cancellationToken)
    {
        var found = (await indices.FindAsync(ContextIndices, cancellationToken)).ToList();
        foreach (var name in ContextIndices.Where(n => found.All(f => f.Label != n.Label)))
        {
            if (reported.FirstTime(name.Label))
            {
                LiveFeedLog.IndexMissing(logger, name.Label);
            }
        }

        if ((await benchmark.GetAsync(cancellationToken))?.Instrument is { } chosen
            && found.All(f => f.InstrumentId != chosen.Id)
            && await indices.FindByIdAsync(chosen.Id, cancellationToken) is { } benchmarkIndex)
        {
            found.Add(benchmarkIndex);
        }

        return [.. found.Select(i => new ActiveSetCandidate(i.InstrumentId, FeedSegments.Index, i.SecurityId, FeedMode.Ticker, ActiveSetReason.Index))];
    }
}

/// <summary>Remembers which missing indices were reported already, for the life of the process (the providers are scoped and re-created every minute).</summary>
internal sealed class MissingIndexReport
{
    private readonly object _gate = new();
    private readonly HashSet<string> _labels = new(StringComparer.Ordinal);

    /// <summary>True the first time the label is asked about.</summary>
    public bool FirstTime(string label)
    {
        lock (_gate)
        {
            return _labels.Add(label);
        }
    }
}
