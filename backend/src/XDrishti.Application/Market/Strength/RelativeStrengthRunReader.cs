using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Market.Strength;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Domain.Market.Strength;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Market.Strength;

/// <summary>
/// What a Lab run reads of the relative-strength tables (BK-09): the stored rows of each instrument as of a session before the window end (the frame then reads the newest row strictly before each date, so a decision on day D sees the previous closed session), the pre-registered baskets found with
/// their membership basis and parameter hash, and the record of both for the run header. One <see cref="BeginAsync"/> per run; reads only.
/// </summary>
internal sealed class RelativeStrengthRunReader(IRelativeStrengthStore store, IBasketQueries baskets, IDerivedVersionStore derived, IHoldingClassifications classifications) : IRelativeStrengthRunSource
{
    /// <summary>Calendar days before the window start whose rows are read, so the first day of the window finds the row of the session before it.</summary>
    private const int LeadDays = 14;

    private const int HistoryTake = 20_000;

    public async ValueTask<IRelativeStrengthRun> BeginAsync(IReadOnlyCollection<Guid> instrumentIds, DateOnly to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);
        var all = await baskets.ListAsync(false, cancellationToken);
        var found = new List<(RelativeStrengthBasis Basis, int Code)>();
        for (var code = 1; code <= StrengthBaskets.Names.Count; code++)
        {
            var name = StrengthBaskets.Names[code - 1];
            if (StrengthBasketNames.Find(all, name) is not { } basket)
            {
                continue;
            }

            var history = await baskets.HistoryAsync(basket.Id, HistoryTake, cancellationToken);
            var days = history?.Items.Select(e => IndiaTime.Today(e.At)).Distinct().Count() ?? 0;
            string? hash = null;
            if (basket.Kind == BasketKinds.Derived && await derived.ListVersionsAsync(basket.Id, 0, 1, cancellationToken) is { Items: [var newest, ..] })
            {
                hash = newest.ParamsHash;
            }

            found.Add((new RelativeStrengthBasis(basket.Id, name, hash, days >= 2 ? ComputeRelativeStrengthHandler.Dated : ComputeRelativeStrengthHandler.SingleSnapshot), code));
        }

        var sectors = instrumentIds.Count == 0 ? new Dictionary<Guid, string>() : new Dictionary<Guid, string>(await classifications.SectorsAsync(instrumentIds, cancellationToken));
        return new RelativeStrengthRun(this, found.ToDictionary(f => f.Basis.BasketId, f => f.Code), [.. found.Select(f => f.Basis)], sectors, to);
    }

    internal async ValueTask<RelativeStrengthInputs> ReadAsync(Guid instrumentId, string? sector, IReadOnlyDictionary<Guid, int> codes, DateOnly from, DateOnly to, ConcurrentDictionary<string, Task<IReadOnlyList<SectorRow>>> sectorCache, CancellationToken cancellationToken)
    {
        var first = from.AddDays(-LeadDays);
        var last = to.AddDays(-1);
        var points = new List<StrengthPoint>();
        foreach (var r in await store.ForInstrumentAsync(instrumentId, first, last, cancellationToken))
        {
            var value = r.State == "value";
            if (r.BasketId == Guid.Empty)
            {
                points.Add(new StrengthPoint("rs_vs_index", r.Lookback, 0, 0, r.AsOf, value && r.Score is { } s ? s : double.NaN));
            }
            else if (codes.TryGetValue(r.BasketId, out var code))
            {
                points.Add(new StrengthPoint("mom_rank", code, r.Lookback, r.Skip, r.AsOf, value && r.PctRank is { } p ? p : double.NaN));
            }
        }

        if (!string.IsNullOrWhiteSpace(sector))
        {
            foreach (var r in await sectorCache.GetOrAdd(sector.Trim(), key => store.SectorRowsAsync(key, first, last, cancellationToken).AsTask()))
            {
                points.Add(new StrengthPoint("sector_rs", r.WindowSessions, 0, 0, r.AsOf, r.State == "value" && r.Rank is { } k ? k : double.NaN));
            }
        }

        return new RelativeStrengthInputs(points);
    }
}

/// <summary>The state of one run's reading: what each instrument got, and the record for the run header (hash of all rows read, baskets with basis and parameter hash).</summary>
internal sealed class RelativeStrengthRun(RelativeStrengthRunReader reader, IReadOnlyDictionary<Guid, int> codes, IReadOnlyList<RelativeStrengthBasis> baskets, IReadOnlyDictionary<Guid, string> sectors, DateOnly to) : IRelativeStrengthRun
{
    private readonly ConcurrentDictionary<Guid, (string Digest, int Rows)> _read = new();

    // the sector rows are the same for every share of the sector: read once per run
    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<SectorRow>>> _sectorRows = new(StringComparer.Ordinal);

    public async ValueTask<RelativeStrengthInputs> ReadAsync(Guid instrumentId, DateOnly from, CancellationToken cancellationToken)
    {
        var inputs = await reader.ReadAsync(instrumentId, sectors.GetValueOrDefault(instrumentId), codes, from, to, _sectorRows, cancellationToken);
        var text = new StringBuilder();
        foreach (var p in inputs.Points.OrderBy(p => p.Feature, StringComparer.Ordinal).ThenBy(p => p.P1).ThenBy(p => p.P2).ThenBy(p => p.P3).ThenBy(p => p.AsOf))
        {
            text.Append(CultureInfo.InvariantCulture, $"{p.Feature}|{p.P1}|{p.P2}|{p.P3}|{p.AsOf:yyyy-MM-dd}|{(double.IsNaN(p.Value) ? "-" : p.Value.ToString("R", CultureInfo.InvariantCulture))}\n");
        }

        _read[instrumentId] = (CanonicalText.Sha256Hex(text.ToString()), inputs.Points.Count);
        return inputs;
    }

    /// <summary>The record of what was read, for the run header: the same rows give the same hash whatever the order the instruments were read in.</summary>
    public RelativeStrengthRecord Record(long featureUnknown = 0)
    {
        var all = string.Join('\n', _read.OrderBy(kv => kv.Key).Select(kv => kv.Key.ToString("D") + "|" + kv.Value.Digest));
        return new RelativeStrengthRecord(CanonicalText.Sha256Hex(all), _read.Values.Sum(v => v.Rows), to, baskets, RelativeStrengthRecord.CurrentVersion, featureUnknown);
    }
}
