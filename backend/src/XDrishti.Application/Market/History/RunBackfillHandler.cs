using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Market.Actions;
using XDrishti.Application.Market.Bars.Csv;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.History;

namespace XDrishti.Application.Market.History;

/// <summary>Runs the 1-minute backfill (CF-04 Part B). <paramref name="InstrumentId"/> limits it to one instrument; <paramref name="DryRun"/> plans, checks the calendar and the disk and reports the estimate, and requests and writes nothing.</summary>
public sealed record RunBackfillCommand(Guid? InstrumentId = null, bool DryRun = false) : ICommand;

/// <summary>What one run did. <see cref="Stopped"/> is null when the run went through every pending chunk, else <c>dhan_not_connected</c>.</summary>
public sealed record BackfillSummary(
    bool DryRun,
    int Instruments,
    int InstrumentsWithoutCsv,
    int InstrumentsUnresolved,
    int ChunksPending,
    int ChunksDone,
    int ChunksEmpty,
    int ChunksFailed,
    long Rows,
    int BeyondSeam,
    int SeamJumps,
    int Requests,
    long EstimatedRows,
    long EstimatedRequests,
    double EstimatedMinutes,
    string? Stopped)
{
    public string Text
    {
        get
        {
            var head = string.Create(
                CultureInfo.InvariantCulture,
                $"{(DryRun ? "dry run: " : string.Empty)}{Instruments} instruments ({InstrumentsWithoutCsv} without CSV bars, {InstrumentsUnresolved} seam_unresolved), {ChunksPending} chunks pending (about {EstimatedRows:N0} rows, {EstimatedRequests:N0} requests, at least {EstimatedMinutes:F1} minutes); ");
            var tail = DryRun
                ? "nothing requested"
                : string.Create(CultureInfo.InvariantCulture, $"{ChunksDone} done, {ChunksEmpty} empty, {ChunksFailed} failed, {Rows:N0} rows written, {BeyondSeam} beyond the seam dropped, {SeamJumps} seam jumps, {Requests} requests");
            return head + tail + (Stopped is null ? string.Empty : ", stopped: " + Stopped);
        }
    }
}

/// <summary>
/// The Dhan 1-minute backfill (CF-04 Part B). OFF by default: with <c>Data:History:Backfill:Enabled</c> off it returns <c>history.disabled</c> before it reads anything or sends anything. It writes only before each instrument's first CSV bar: the windows end at the seam
/// (<see cref="BackfillPlanner"/>) and the writer drops any bar at or after it and counts it <c>beyond_seam</c> (<see cref="SeamGuard"/>); the seam is read again from the stored bars on every run. It refuses to start without the daily-file calendar
/// (<c>history.calendar_missing</c>) or with too little disk (<c>history.disk_low</c>). The chunk ledger makes it resumable and idempotent: a <c>done</c> or <c>empty</c> chunk is never asked for again, a <c>failed</c> one at most <c>MaxRetries</c> times in all; the
/// newest chunks come first. A failed chunk never fails the run. An instrument whose overlap probe failed is not backfilled. When an instrument's chunks are all finished its seam is checked for a jump.
/// </summary>
internal sealed class RunBackfillHandler(
    IBackfillSettings settings,
    IDataSettings dataSettings,
    IIntradayBarSource source,
    IDataAccountStatus account,
    IFetchTargets targets,
    IBarStore bars,
    IBackfillStore store,
    IBackfillCalendar calendar,
    IFreeDiskProbe disk,
    ICorporateActionQueries actionQueries,
    IAdjustmentFactors adjustments,
    TimeProvider clock,
    ILogger<RunBackfillHandler> logger) : ICommandHandler<RunBackfillCommand, BackfillSummary>
{
    private const int MaxErrorLength = 200;

    public async ValueTask<Result<BackfillSummary>> HandleAsync(RunBackfillCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!settings.Enabled)
        {
            return Result.Failure<BackfillSummary>(BackfillErrors.Disabled());
        }

        var list = await targets.ListAsync(cancellationToken);
        if (command.InstrumentId is { } only)
        {
            list = [.. list.Where(t => t.InstrumentId == only)];
            if (list.Count == 0)
            {
                return Result.Failure<BackfillSummary>(BackfillErrors.NoTarget());
            }
        }

        var today = IndiaTime.Today(clock.GetUtcNow());
        var stats = new Stats();
        var works = await PlanAsync(list, today, command.DryRun, stats, cancellationToken);
        var pending = works.Sum(w => w.Pending.Count);
        var estimate = BackfillEstimate.Of(works.Sum(w => w.Pending.Sum(c => BackfillEstimate.RowsOfDays(c.Days))), pending, dataSettings.DhanRequestsPerSecond);
        if (pending > 0)
        {
            var refusal = await CheckStartAsync(works, estimate, cancellationToken);
            if (refusal is { } error)
            {
                return Result.Failure<BackfillSummary>(error);
            }
        }

        if (command.DryRun || pending == 0)
        {
            return Result.Success(Summary(command.DryRun, list.Count, pending, estimate, stats, null));
        }

        var availability = await source.CheckAsync(cancellationToken);
        if (!availability.Available)
        {
            return Result.Failure<BackfillSummary>(Error.Conflict(DataErrors.DhanNotAvailableCode, availability.Reason ?? "The Dhan Data API fetch is not available."));
        }

        var state = await account.GetAsync(cancellationToken);
        if (!state.CanFetch)
        {
            return Result.Failure<BackfillSummary>(Error.Conflict(DataErrors.DhanNotConnectedCode, state.Reason ?? "There is no connected primary Data account."));
        }

        foreach (var work in works)
        {
            await store.PlanChunksAsync(work.Target.InstrumentId, [.. work.Chunks.Select(c => (c.From, c.To))], cancellationToken);
        }

        string? stopped = null;
        foreach (var work in works)
        {
            stopped = await RunInstrumentAsync(work, stats, cancellationToken);
            if (stopped is not null)
            {
                break;
            }
        }

        return Result.Success(Summary(false, list.Count, pending, estimate, stats, stopped));
    }

    private static BackfillSummary Summary(bool dryRun, int instruments, int pending, BackfillEstimate estimate, Stats stats, string? stopped) =>
        new(dryRun, instruments, stats.WithoutCsv, stats.Unresolved, pending, stats.Done, stats.Empty, stats.Failed, stats.Rows, stats.BeyondSeam, stats.Jumps, stats.Requests, estimate.Rows, estimate.Requests, estimate.MinutesAtRate, stopped);

    // The seam is read again from the stored bars every run. A seam that moved earlier (a CSV import reached before it) leaves the instrument unresolved until it is probed again; the planner then uses the new seam.
    private async ValueTask<List<Work>> PlanAsync(IReadOnlyList<FetchTarget> list, DateOnly today, bool dryRun, Stats stats, CancellationToken cancellationToken)
    {
        var works = new List<Work>();
        var records = await store.ListSeamsAsync([.. list.Select(t => t.InstrumentId)], cancellationToken);
        foreach (var target in list)
        {
            var seam = await store.CurrentSeamAsync(target.InstrumentId, cancellationToken);
            if (seam is null)
            {
                stats.WithoutCsv++;
                continue;
            }

            var known = records.GetValueOrDefault(target.InstrumentId);
            var record = known ?? new SeamRecord(target.InstrumentId, seam.Value, ProbeStatus.None, null, null, null, null, false, null);
            if (record.SeamAt != seam.Value)
            {
                var moved = seam.Value < record.SeamAt;
                if (moved)
                {
                    BackfillLog.SeamMoved(logger, target.Symbol);
                }

                record = record with
                {
                    SeamAt = seam.Value,
                    ProbeStatus = moved ? ProbeStatus.Unresolved : record.ProbeStatus,
                    Detail = moved ? "a CSV import reached before the recorded seam; probe again" : record.Detail,
                };
            }

            if (!dryRun && (known is null || known.SeamAt != record.SeamAt))
            {
                await store.SaveSeamAsync(record, cancellationToken);
            }

            if (record.ProbeStatus == ProbeStatus.Unresolved)
            {
                stats.Unresolved++;
                continue;
            }

            var chunks = BackfillPlanner.Chunks(IndiaTime.Today(seam.Value), today, settings.Years, settings.ChunkDays);
            var ledger = (await store.ListChunksAsync(target.InstrumentId, cancellationToken)).ToDictionary(c => c.From);
            var pending = chunks.Where(c => IsPending(ledger.GetValueOrDefault(c.From), c.To)).ToList();
            works.Add(new Work(target, seam.Value, chunks, pending, ledger));
        }

        return works;
    }

    private bool IsPending(ChunkRecord? chunk, DateOnly to) =>
        chunk is null || chunk.To != to || chunk.Status == ChunkStatus.Planned || (chunk.Status == ChunkStatus.Failed && chunk.Attempts < settings.MaxRetries);

    // Calendar before data, then the disk.
    private async ValueTask<Error?> CheckStartAsync(List<Work> works, BackfillEstimate estimate, CancellationToken cancellationToken)
    {
        var from = works.Min(w => w.Pending.Min(c => c.From));
        var to = works.Max(w => w.Pending.Max(c => c.To)).AddDays(-1);
        var known = await calendar.KnownDaysAsync(from, to, cancellationToken);
        var missing = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !known.Contains(d))
            {
                missing++;
            }
        }

        if (missing > 0)
        {
            return BackfillErrors.CalendarMissing(from, to, missing);
        }

        var free = disk.FreeBytes();
        return BackfillDiskGuard.Enough(free, estimate.Rows, settings.BytesPerRowEstimate, settings.DiskHeadroomFactor)
            ? null
            : BackfillErrors.DiskLow(free, BackfillDiskGuard.RequiredBytes(estimate.Rows, settings.BytesPerRowEstimate, settings.DiskHeadroomFactor), estimate.Rows);
    }

    private async ValueTask<string?> RunInstrumentAsync(Work work, Stats stats, CancellationToken cancellationToken)
    {
        var target = work.Target;
        foreach (var chunk in work.Pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = work.Ledger.GetValueOrDefault(chunk.From);
            var attempts = (previous is { } p && p.To == chunk.To ? p.Attempts : 0) + 1;
            var outcome = await RunChunkAsync(work, chunk, attempts, stats, cancellationToken);
            if (outcome.Unauthorized)
            {
                return "dhan_not_connected";
            }

            await store.SaveChunkAsync(new ChunkRecord(target.InstrumentId, chunk.From, chunk.To, outcome.Status, outcome.Rows, attempts, outcome.Error), cancellationToken);
            work.Ledger[chunk.From] = new ChunkRecord(target.InstrumentId, chunk.From, chunk.To, outcome.Status, outcome.Rows, attempts, outcome.Error);
        }

        if (work.Chunks.All(c => work.Ledger.GetValueOrDefault(c.From) is { Status: ChunkStatus.Done or ChunkStatus.Empty } finished && finished.To == c.To))
        {
            await CheckSeamAsync(work, stats, cancellationToken);
        }

        return null;
    }

    private async ValueTask<ChunkOutcome> RunChunkAsync(Work work, BackfillChunk chunk, int attempts, Stats stats, CancellationToken cancellationToken)
    {
        var target = work.Target;
        var window = new FetchWindow(Midnight(chunk.From), Midnight(chunk.To));
        var plan = new IntradayFetchPlan([new IntradayTarget(target.InstrumentId, target.Symbol, target.Type, target.SecurityId, target.Segment, [window])]);
        IntradayResult? result = null;
        await foreach (var r in source.FetchAsync(plan, cancellationToken))
        {
            result = r;
            break;
        }

        stats.Requests += result?.Requests ?? 1;
        if (result is { Failure.Kind: IntradayFailureKind.Unauthorized })
        {
            return ChunkOutcome.NotConnected;
        }

        if (result is null || result.Failure is not null)
        {
            BackfillLog.ChunkFailed(logger, target.Symbol, chunk.From, chunk.To, attempts);
            stats.Failed++;
            return ChunkOutcome.Fail(Trim(result?.Failure?.Message ?? "Dhan gave no answer"));
        }

        try
        {
            var checkedBars = IntradayBarCheck.Check(result.Bars);
            var (allowed, beyond) = SeamGuard.Split(checkedBars.Bars, work.Seam);
            if (beyond > 0)
            {
                stats.BeyondSeam += beyond;
                BackfillLog.BeyondSeam(logger, target.Symbol, beyond);
            }

            if (allowed.Count > 0)
            {
                await WriteAsync(target.InstrumentId, allowed, cancellationToken);
            }

            stats.Rows += allowed.Count;
            if (allowed.Count == 0)
            {
                stats.Empty++;
                return new ChunkOutcome(ChunkStatus.Empty, 0, null, false);
            }

            stats.Done++;
            return new ChunkOutcome(ChunkStatus.Done, allowed.Count, null, false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // one chunk failing (for example the database refusing its bars) must not stop the others; it is recorded as failed and logged
        catch (Exception ex)
#pragma warning restore CA1031
        {
            BackfillLog.ChunkCrashed(logger, target.Symbol, chunk.From, chunk.To, ex);
            stats.Failed++;
            return ChunkOutcome.Fail("the bars could not be stored");
        }
    }

    private async ValueTask WriteAsync(Guid instrumentId, IReadOnlyList<Bar> allowed, CancellationToken cancellationToken)
    {
        await using var session = await bars.BeginMergeAsync(instrumentId, BarSource.Dhan, cancellationToken);
        var all = allowed.ToArray();
        for (var offset = 0; offset < all.Length; offset += CsvBarReader.BatchSize)
        {
            await session.AddAsync(all.AsMemory(offset, Math.Min(CsvBarReader.BatchSize, all.Length - offset)), cancellationToken);
        }

        await session.CompleteAsync(null, cancellationToken);
    }

    // The adjusted close of the last pre-seam session against the first CSV open, both on today's basis; a move beyond 20 % that no applied action explains is a seam_jump.
    private async ValueTask CheckSeamAsync(Work work, Stats stats, CancellationToken cancellationToken)
    {
        var id = work.Target.InstrumentId;
        var before = await bars.ReadAsync(id, work.Seam.AddDays(-10), work.Seam.AddMinutes(-1), cancellationToken);
        var after = await bars.ReadAsync(id, work.Seam, work.Seam.AddDays(1), cancellationToken);
        if (before.Count == 0 || after.Count == 0)
        {
            return;
        }

        var through = (await actionQueries.ListCsvAdjustedThroughAsync([id], cancellationToken)).TryGetValue(id, out var date) ? date : (DateOnly?)null;
        var factors = new SourceAwareFactors(await adjustments.GetAsync(id, cancellationToken), IndiaTime.Today(work.Seam), through);
        var last = before[^1];
        var first = after[0];
        var lastClose = factors.Price(last.Close, SessionClock.DateOf(last.EpochSeconds));
        var firstOpen = factors.Price(first.Open, SessionClock.DateOf(first.EpochSeconds));
        var jump = SeamContinuity.IsJump(lastClose, firstOpen);
        var move = SeamContinuity.Move(lastClose, firstOpen);
        var record = (await store.ListSeamsAsync([id], cancellationToken)).GetValueOrDefault(id);
        if (record is null || record.SeamJump != jump)
        {
            await store.SaveSeamAsync((record ?? new SeamRecord(id, work.Seam, ProbeStatus.None, null, null, null, null, false, null)) with { SeamJump = jump, JumpPct = move * 100 }, cancellationToken);
        }

        if (jump)
        {
            stats.Jumps++;
            BackfillLog.SeamJump(logger, work.Target.Symbol, move * 100);
        }
    }

    private static DateTimeOffset Midnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);

    private static string Trim(string text) => text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];

    private sealed record Work(FetchTarget Target, DateTimeOffset Seam, IReadOnlyList<BackfillChunk> Chunks, List<BackfillChunk> Pending, Dictionary<DateOnly, ChunkRecord> Ledger);

    private sealed record ChunkOutcome(string Status, int Rows, string? Error, bool Unauthorized)
    {
        public static ChunkOutcome NotConnected { get; } = new(ChunkStatus.Failed, 0, null, true);

        public static ChunkOutcome Fail(string error) => new(ChunkStatus.Failed, 0, error, false);
    }

    private sealed class Stats
    {
        public int WithoutCsv { get; set; }

        public int Unresolved { get; set; }

        public int Done { get; set; }

        public int Empty { get; set; }

        public int Failed { get; set; }

        public long Rows { get; set; }

        public int BeyondSeam { get; set; }

        public int Jumps { get; set; }

        public int Requests { get; set; }
    }
}
