using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.SimAll;

namespace XDrishti.Application.Tests.Market.History;

internal sealed class FakeBackfillSettings : IBackfillSettings
{
    public bool Enabled { get; set; } = true;

    public bool AdmitVerified { get; set; }

    public int Years { get; set; } = 5;

    public int ChunkDays { get; set; } = 90;

    public int MaxRetries { get; set; } = 3;

    public int DiskHeadroomFactor { get; set; } = 2;

    public int BytesPerRowEstimate { get; set; } = 60;

    public TimeOnly RunWindowFrom { get; set; } = new(20, 0);

    public TimeOnly RunWindowTo { get; set; } = new(6, 0);

    public int ProbeInstruments { get; set; } = 20;

    public int ProbeSessions { get; set; } = 10;

    public double ProbeMedianBps { get; set; } = 5;

    public double ProbeMaxBps { get; set; } = 50;

    public double ProbeMinWithinMaxPct { get; set; } = 99;
}

/// <summary>The ledger and the seam rows in memory. The seam of an instrument is whatever the test put in CurrentSeams (the oldest CSV bar).</summary>
internal sealed class FakeBackfillStore : IBackfillStore
{
    public Dictionary<Guid, DateTimeOffset> CurrentSeams { get; } = [];

    public Dictionary<Guid, SeamRecord> Records { get; } = [];

    public Dictionary<(Guid, DateOnly), ChunkRecord> Chunks { get; } = [];

    public int SeamWrites { get; private set; }

    public int ChunkWrites { get; private set; }

    public ValueTask<DateTimeOffset?> CurrentSeamAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<DateTimeOffset?>(CurrentSeams.TryGetValue(instrumentId, out var seam) ? seam : null);

    public ValueTask<IReadOnlyDictionary<Guid, SeamRecord>> ListSeamsAsync(IReadOnlyCollection<Guid> instrumentIds, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyDictionary<Guid, SeamRecord>>(Records.Where(r => instrumentIds.Contains(r.Key)).ToDictionary(r => r.Key, r => r.Value));

    public ValueTask SaveSeamAsync(SeamRecord seam, CancellationToken cancellationToken)
    {
        SeamWrites++;
        Records[seam.InstrumentId] = seam;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ChunkRecord>> ListChunksAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ChunkRecord>>([.. Chunks.Where(c => c.Key.Item1 == instrumentId).Select(c => c.Value).OrderByDescending(c => c.From)]);

    public ValueTask PlanChunksAsync(Guid instrumentId, IReadOnlyList<(DateOnly From, DateOnly To)> chunks, CancellationToken cancellationToken)
    {
        foreach (var (from, to) in chunks)
        {
            if (!Chunks.TryGetValue((instrumentId, from), out var existing) || existing.To != to)
            {
                Chunks[(instrumentId, from)] = new ChunkRecord(instrumentId, from, to, "planned", 0, 0, null);
            }
        }

        foreach (var stale in Chunks.Where(c => c.Key.Item1 == instrumentId && c.Value.Status == "planned" && !chunks.Any(x => x.From == c.Key.Item2)).Select(c => c.Key).ToList())
        {
            Chunks.Remove(stale);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask SaveChunkAsync(ChunkRecord chunk, CancellationToken cancellationToken)
    {
        ChunkWrites++;
        Chunks[(chunk.InstrumentId, chunk.From)] = chunk;
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ChunkCounts>> CountChunksAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult<IReadOnlyList<ChunkCounts>>(
        [
            .. Chunks.GroupBy(c => c.Key.Item1).Select(g => new ChunkCounts(g.Key, g.Count(), g.Count(c => c.Value.Status is "done" or "empty"), g.Count(c => c.Value.Status == "failed"))),
        ]);
}

internal sealed class FakeBackfillCalendar : IBackfillCalendar
{
    /// <summary>When true every date asked for is known; else only <see cref="Known"/>.</summary>
    public bool AllKnown { get; set; } = true;

    public HashSet<DateOnly> Known { get; } = [];

    public ValueTask<IReadOnlySet<DateOnly>> KnownDaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var days = new HashSet<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (AllKnown || Known.Contains(d))
            {
                days.Add(d);
            }
        }

        return ValueTask.FromResult<IReadOnlySet<DateOnly>>(days);
    }
}

internal sealed class FakeDisk(long? free) : IFreeDiskProbe
{
    public long? FreeBytes() => free;
}

/// <summary>
/// The high-water seams in memory; an instrument without an entry has none (every bar is read). <see cref="Facts"/> are the stored facts of the admission rule (CF-04d); an instrument without entry answers like a switch that is off. <see cref="DhanInSpan"/> says
/// for which instruments a dhan bar is stored inside the CSV span.
/// </summary>
internal sealed class FakeSeams : IBackfillSeams
{
    public Dictionary<Guid, DateTimeOffset> Cuts { get; } = [];

    public Dictionary<Guid, SeamAdmissionFacts> Facts { get; } = [];

    public HashSet<Guid> DhanInSpan { get; } = [];

    public ValueTask<DateTimeOffset?> SeamOfAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<DateTimeOffset?>(Cuts.TryGetValue(instrumentId, out var cut) ? cut : null);

    public bool AdmitVerified { get; set; } = true;

    public ValueTask<SeamAdmissionFacts?> AdmissionFactsAsync(Guid instrumentId, CancellationToken cancellationToken) =>
        ValueTask.FromResult<SeamAdmissionFacts?>(Facts.TryGetValue(instrumentId, out var facts) ? facts : SeamAdmissionFacts.Off);

    public ValueTask<bool> HasDhanBarsAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        ValueTask.FromResult(DhanInSpan.Contains(instrumentId));
}
