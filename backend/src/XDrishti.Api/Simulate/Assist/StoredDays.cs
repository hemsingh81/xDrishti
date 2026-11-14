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
    XDrishti.Application.Abstractions.Market.History.IBackfillSeams seams,
    XDrishti.Application.Market.Actions.IAdjustmentFactors factors) : IStoredDays
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    private readonly Dictionary<string, (Guid Id, decimal Tick, bool Fallback)?> _known = new(StringComparer.Ordinal);

    public async ValueTask<StoredDay> GetAsync(string symbol, DateOnly date, CancellationToken cancellationToken)
    {
        var (tick, fallback, read) = await ReadAsync(symbol, date, date, cancellationToken);
        return new StoredDay(tick, read, fallback);
    }

    public async ValueTask<StoredRange> GetRangeAsync(string symbol, DateOnly from, DateOnly to, DateOnly lastUsed, DateOnly? admittedSeam, CancellationToken cancellationToken)
    {
        var (tick, _, read) = await ReadAsync(symbol, from, to, cancellationToken);
        var caveat = _known.GetValueOrDefault(symbol) is { } instrument ? await CaveatAsync(factors, instrument.Id, from, to, lastUsed, admittedSeam, cancellationToken) : null;
        return new StoredRange(tick, Sessions(read), caveat);
    }

    /// <summary>One session per India date, ascending. Pure; the date is the India date of the bar start.</summary>
    internal static List<ReaderSession> Sessions(IReadOnlyList<ReaderBar> read) =>
        [.. read
            .GroupBy(b => DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(b.EpochSeconds).ToOffset(India).DateTime))
            .OrderBy(g => g.Key)
            .Select(g => new ReaderSession(g.Key, [.. g]))];

    /// <summary>
    /// The raw stored bars of a range are not the engine's when an applied corporate action's ex-date lies after <paramref name="from"/> and up to <paramref name="lastUsed"/> (the engine adjusts the older side; the factor at the first date differs from the one at the last used date),
    /// or when the run admitted pre-seam bars (<paramref name="admittedSeam"/>, from the run record) from a seam inside the range. With admission off the engine and the reader see the same cut bars, so a seam alone is no caveat. The multi-session readers adjust nothing (ADR 0291).
    /// shortcut: an action after <paramref name="lastUsed"/> scales every bar of the range evenly and is not flagged (the same gap as ORB's adjusted_instrument); the ratios the readers use are unchanged by it.
    /// </summary>
    internal static async Task<string?> CaveatAsync(XDrishti.Application.Market.Actions.IAdjustmentFactors factors, Guid instrumentId, DateOnly from, DateOnly to, DateOnly lastUsed, DateOnly? admittedSeam, CancellationToken cancellationToken)
    {
        var applied = await factors.GetAsync(instrumentId, cancellationToken);
        if (applied.FactorAt(from) != applied.FactorAt(lastUsed))
        {
            var action = applied.ActionsAfter(from).First(a => a.ExDate <= lastUsed);
            return "a corporate action (ex-date " + action.ExDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ") lies inside the history the reader used, which is raw and unadjusted";
        }

        if (admittedSeam is { } seam && seam >= from && seam <= to)
        {
            return "the run admitted pre-seam bars from the seam (" + seam.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ") inside the history the reader used, which is raw and not source-aware";
        }

        return null;
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
