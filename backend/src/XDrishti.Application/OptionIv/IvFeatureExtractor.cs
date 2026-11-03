using System.Globalization;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.OptionExpression;
using XDrishti.Application.Abstractions.OptionIv;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Options;
using XDrishti.Domain.Options.Iv;
using XDrishti.Domain.Options.Quality;
using XDrishti.Domain.Options.Remark;

namespace XDrishti.Application.OptionIv;

/// <summary>
/// The <c>iv-1</c> extraction (P14-11, ADR 0199): one underlying at a time, never the whole archive. For each option underlying that has trades (and NIFTY, for the market-wide rank) it reads the daily ATM IV series in one grouped query, the quality verdicts of the seven-offset key-sessions it needs in one filtered query, and the bars of the
/// trade dates in 31-day stretches at the seven offsets only; then the pure functions of <see cref="IvFeatures"/> compute each trade from what had ended at its instant. A trade whose instrument is not an option underlying of the archive reads nothing but the NIFTY series. Nothing is filled: an unknown value stays NaN with its reason.
/// </summary>
internal sealed class IvFeatureExtractor(
    IRollingBarReader bars,
    IRollingQualityRuns quality,
    IOptionReference reference,
    IContractExpiryReader contracts,
    IBarQueries dailyBars,
    IAdjustmentFactors adjustments) : IIvFeatureExtractor
{
    private const int ChunkDays = 31;
    private const int HistoryDays = 420;
    private const int ClosesDays = 70;
    private const int DailyWindowDays = IRollingBarReader.MaxDailyRangeDays - 1;
    private static readonly string[] Sides = ["CE", "PE"];

    public async ValueTask<IvExtraction> ExtractAsync(IReadOnlyList<IvTrade> trades, EventCalendarSnapshot snapshot, TradingCalendar calendar, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(calendar);
        var cells = new Dictionary<string, FeatureCell[]>(trades.Count, StringComparer.Ordinal);
        var runIds = new SortedSet<long>();
        long watermark = 0;
        if (trades.Count == 0)
        {
            return new IvExtraction(cells, new IvSources(0, [], IvLabels.Unit, false));
        }

        var epochs = await reference.EpochsAsync(cancellationToken);
        var ids = trades.Select(t => t.InstrumentId).Distinct().Order().ToList();
        var symbols = await reference.SymbolsAsync(ids, cancellationToken);
        var first = trades.Min(t => IndiaTime.Today(t.At));
        var last = trades.Max(t => IndiaTime.Today(t.At));
        var niftyId = await reference.FindUnderlyingAsync(ExpiryRules.Nifty, cancellationToken);
        Underlying? nifty = null;
        if (niftyId is { } nid && await bars.HasSeriesAsync(nid, cancellationToken))
        {
            nifty = await LoadAsync(nid, ExpiryRules.Nifty, first, last, epochs, calendar, runIds, cancellationToken);
            watermark = Math.Max(watermark, nifty.Watermark);
        }

        foreach (var group in trades.GroupBy(t => t.InstrumentId).OrderBy(g => g.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var own = !symbols.TryGetValue(group.Key, out var symbol) || !await bars.HasSeriesAsync(group.Key, cancellationToken)
                ? null
                : group.Key == niftyId && nifty is not null ? nifty : await LoadAsync(group.Key, symbol, first, last, epochs, calendar, runIds, cancellationToken);
            var rows = snapshot.RowsOf(group.Key);
            var ordered = group.OrderBy(t => t.At).ThenBy(t => t.TradeKey, StringComparer.Ordinal).ToList();
            IReadOnlyDictionary<DateOnly, double> closes = new Dictionary<DateOnly, double>();
            IReadOnlyList<Domain.Market.Actions.AppliedAction> actions = [];
            if (own is not null)
            {
                watermark = Math.Max(watermark, own.Watermark);
                var days = await dailyBars.ReadDailyBarsAsync(group.Key, IndiaTime.Today(ordered[0].At).AddDays(-ClosesDays), IndiaTime.Today(ordered[^1].At).AddDays(1), cancellationToken);
                closes = days.Where(d => d.Complete).ToDictionary(d => IndiaTime.Today(d.Ts), d => d.Close);
                actions = (await adjustments.GetAsync(group.Key, cancellationToken)).Actions;
            }

            foreach (var chunk in Chunks(ordered))
            {
                var book = own is null ? null : await ReadBookAsync(group.Key, own, chunk, cancellationToken);
                foreach (var trade in chunk)
                {
                    var date = IndiaTime.Today(trade.At);
                    var plan = own is null ? null : IvSeries.Plan(own.Symbol, date, own.Expiries, calendar);
                    cells[trade.TradeKey] = IvFeatures.Compute(new IvFeatureInput(trade.At, calendar, plan, book, own?.Daily, nifty?.Daily, closes, actions, rows, TimeSpan.Zero));
                }
            }
        }

        return new IvExtraction(cells, new IvSources(watermark, [.. runIds], IvLabels.Unit, nifty is not null));
    }

    // The underlying's expiry calendar, its daily ATM IV series and the usable key-sessions of the seven offsets, read once for the range of all trades.
    private async ValueTask<Underlying> LoadAsync(Guid id, string symbol, DateOnly first, DateOnly last, IReadOnlyList<MarketEpoch> epochs, TradingCalendar calendar, SortedSet<long> runIds, CancellationToken cancellationToken)
    {
        var from = first.AddDays(-HistoryDays);
        var flag = IvSeries.FlagOf(symbol);
        var held = IvSeries.HeldCodes(symbol);
        var interval = IvSeries.IntervalOf(symbol);
        var expiries = new ExpiryCalendar(symbol, await contracts.ExpiriesAsync(id, from.AddDays(-7), last.AddDays(120), cancellationToken), epochs, calendar);
        var keys = new List<string>();
        for (var code = 1; code <= held; code++)
        {
            foreach (var side in Sides)
            {
                for (var offset = -IvFeatures.OiOffsets; offset <= IvFeatures.OiOffsets; offset++)
                {
                    keys.Add(FetchKey(id, flag, code, side, interval, offset));
                }
            }
        }

        var usable = new HashSet<string>(StringComparer.Ordinal);
        var results = await quality.RollingResultsForKeysAsync(id, keys, from, last, cancellationToken);
        foreach (var session in results.GroupBy(r => (Key: r.Row.FetchKey!, r.Row.SessionDate)))
        {
            runIds.UnionWith(session.Select(r => r.RunId));
            if (SessionUsability.IsUsable(QualityCodes.Rolling, [.. session.Select(r => r.Row.Check)]))
            {
                usable.Add(KeySession(session.Key.Key, session.Key.SessionDate));
            }
        }

        // The reader refuses a range over MaxDailyRangeDays, and a run can span years: consecutive windows, concatenated.
        var rows = new List<DailySideIv>();
        var codes = Enumerable.Range(1, held).ToArray();
        for (var start = from; start <= last; start = start.AddDays(DailyWindowDays))
        {
            var end = DateOnly.FromDayNumber(Math.Min(start.DayNumber + DailyWindowDays, last.DayNumber + 1));
            rows.AddRange(await bars.DailyLastBarsAsync(id, flag, codes, interval, StartOf(start), StartOf(end), cancellationToken));
        }

        var marked = rows.Select(r => r with { Usable = usable.Contains(KeySession(FetchKey(id, flag, r.Code, r.Side, interval, 0), r.Date)) });
        var daily = DailyAtmIv.Build(marked, date => IvSeries.Plan(symbol, date, expiries, calendar)?.FrontCode);
        var watermark = await bars.WatermarkAsync(id, from, last, cancellationToken);
        return new Underlying(symbol, flag, held, interval, expiries, daily, usable, watermark);
    }

    private async ValueTask<IvBarBook> ReadBookAsync(Guid id, Underlying own, List<IvTrade> chunk, CancellationToken cancellationToken)
    {
        var from = StartOf(chunk.Min(t => IndiaTime.Today(t.At)));
        var to = StartOf(chunk.Max(t => IndiaTime.Today(t.At)).AddDays(1));
        var offsets = Enumerable.Range(-IvFeatures.OiOffsets, (2 * IvFeatures.OiOffsets) + 1).ToArray();
        var book = new IvBarBook();
        for (var code = 1; code <= own.Held; code++)
        {
            foreach (var side in Sides)
            {
                var raw = await bars.BarsAsync(id, own.Flag, code, side, own.Interval, offsets, from, to, cancellationToken);
                book.Add(code, side, own.Interval, raw.Select(b => b with { Usable = own.Usable.Contains(KeySession(FetchKey(id, own.Flag, code, side, own.Interval, b.Offset), IndiaTime.Today(b.Ts))) }));
            }
        }

        return book;
    }

    private static List<List<IvTrade>> Chunks(List<IvTrade> ordered)
    {
        var chunks = new List<List<IvTrade>>();
        DateOnly? start = null;
        foreach (var trade in ordered)
        {
            var date = IndiaTime.Today(trade.At);
            if (start is null || date.DayNumber - start.Value.DayNumber >= ChunkDays)
            {
                chunks.Add([]);
                start = date;
            }

            chunks[^1].Add(trade);
        }

        return chunks;
    }

    private static DateTimeOffset StartOf(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);

    // The fetch key of the archive: underlying, kind and code, offset, side, bar width. A key-session is the key and an India day.
    private static string FetchKey(Guid id, char flag, int code, string side, int interval, int offset) =>
        string.Create(CultureInfo.InvariantCulture, $"{id}:{flag}{code}:{offset}:{side}:{interval}");

    private static string KeySession(string fetchKey, DateOnly date) => string.Create(CultureInfo.InvariantCulture, $"{fetchKey}|{date:yyyy-MM-dd}");

    private sealed record Underlying(string Symbol, char Flag, int Held, int Interval, ExpiryCalendar Expiries, IReadOnlyDictionary<DateOnly, double> Daily, HashSet<string> Usable, long Watermark);
}
