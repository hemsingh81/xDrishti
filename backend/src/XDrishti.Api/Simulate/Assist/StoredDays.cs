using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Search;
using XDrishti.Application.Market.Tracking;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>The stored bars of the assisted check, read through the handlers and the bar store the verbs already use (read only). The tick is the master's, in rupees (the engine's own source), so the readings need nothing from the engine's values.</summary>
internal sealed class StoredDays(
    IQueryHandler<ResolveInstrumentsQuery, Result<ResolveResult>> resolve,
    IQueryHandler<GetInstrumentQuery, Result<InstrumentDetail>> details,
    IBarStore bars,
    XDrishti.Application.Abstractions.Market.History.IBackfillSeams seams) : IStoredDays
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private readonly Dictionary<string, (Guid Id, decimal Tick, bool Fallback)?> _known = new(StringComparer.Ordinal);

    public async ValueTask<StoredDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken)
    {
        var (tick, fallback, read) = await ReadAsync(symbol, date, date, cancellationToken);
        return new StoredDay(tick, read, fallback);
    }

    public async ValueTask<StoredRange> GetRangeAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var (tick, _, read) = await ReadAsync(symbol, from, to, cancellationToken);
        var sessions = read
            .GroupBy(b => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(b.EpochSeconds).ToOffset(India).DateTime))
            .OrderBy(g => g.Key)
            .Select(g => new ReaderSession(g.Key, [.. g]))
            .ToList();
        return new StoredRange(tick, sessions);
    }

    private async Task<(decimal Tick, bool Fallback, IReadOnlyList<ReaderBar> Bars)> ReadAsync(string symbol, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        if (!_known.TryGetValue(symbol, out var known))
        {
            known = await FindAsync(symbol, cancellationToken);
            _known[symbol] = known;
        }

        if (known is not { } instrument)
        {
            return (TickSpec.FallbackSize, true, []);
        }

        // The database accepts only offset 0, so the India-time bounds are passed as UTC.
        var from = new DateTimeOffset(fromDate.ToDateTime(new TimeOnly(9, 0)), India).ToUniversalTime();
        var to = new DateTimeOffset(toDate.ToDateTime(new TimeOnly(15, 45)), India).ToUniversalTime();
        var stored = await XDrishti.Application.Market.History.SeamAwareBars.ReadAsync(bars, seams, instrument.Id, from, to, cancellationToken);
        return (instrument.Tick, instrument.Fallback, [.. stored.Select(b => new ReaderBar(b.EpochSeconds, (decimal)Math.Round(b.Open, 4), (decimal)Math.Round(b.High, 4), (decimal)Math.Round(b.Low, 4), (decimal)Math.Round(b.Close, 4), b.Volume))]);
    }

    private async Task<(Guid, decimal, bool)?> FindAsync(string symbol, CancellationToken cancellationToken)
    {
        var ticker = symbol[(symbol.IndexOf(':', StringComparison.Ordinal) + 1)..];
        var resolved = await resolve.HandleAsync(new ResolveInstrumentsQuery(new ResolveInput([ticker])), cancellationToken);
        if (resolved.IsFailure || resolved.Value.Matched.FirstOrDefault(m => string.Equals(m.Symbol, symbol, StringComparison.Ordinal)) is not { } match)
        {
            return null;
        }

        var detail = await details.HandleAsync(new GetInstrumentQuery(match.Id), cancellationToken);
        var tick = detail.IsFailure ? TickSpec.FromMasterPaise(null) : TickSpec.FromMasterPaise(detail.Value.TickSize);
        return (match.Id, tick.IsSuccess ? tick.Value.Size : TickSpec.FallbackSize, tick.IsFailure);
    }
}
