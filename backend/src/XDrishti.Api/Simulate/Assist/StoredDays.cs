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
    IBarStore bars) : IStoredDays
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private readonly Dictionary<string, (Guid Id, decimal Tick)?> _known = new(StringComparer.Ordinal);

    public async ValueTask<StoredDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken)
    {
        if (!_known.TryGetValue(symbol, out var known))
        {
            known = await FindAsync(symbol, cancellationToken);
            _known[symbol] = known;
        }

        if (known is not { } instrument)
        {
            return new StoredDay(TickSpec.FallbackSize, []);
        }

        var from = new DateTimeOffset(date.ToDateTime(new TimeOnly(9, 0)), India);
        var stored = await bars.ReadAsync(instrument.Id, from, new DateTimeOffset(date.ToDateTime(new TimeOnly(15, 45)), India), cancellationToken);
        return new StoredDay(instrument.Tick, [.. stored.Select(b => new ReaderBar(b.EpochSeconds, (decimal)Math.Round(b.Open, 4), (decimal)Math.Round(b.High, 4), (decimal)Math.Round(b.Low, 4), (decimal)Math.Round(b.Close, 4), b.Volume))]);
    }

    private async Task<(Guid, decimal)?> FindAsync(string symbol, CancellationToken cancellationToken)
    {
        var ticker = symbol[(symbol.IndexOf(':', StringComparison.Ordinal) + 1)..];
        var resolved = await resolve.HandleAsync(new ResolveInstrumentsQuery(new ResolveInput([ticker])), cancellationToken);
        if (resolved.IsFailure || resolved.Value.Matched.FirstOrDefault(m => string.Equals(m.Symbol, symbol, StringComparison.Ordinal)) is not { } match)
        {
            return null;
        }

        var detail = await details.HandleAsync(new GetInstrumentQuery(match.Id), cancellationToken);
        var tick = detail.IsFailure ? TickSpec.FromMasterPaise(null) : TickSpec.FromMasterPaise(detail.Value.TickSize);
        return (match.Id, tick.IsSuccess ? tick.Value.Size : TickSpec.FallbackSize);
    }
}
