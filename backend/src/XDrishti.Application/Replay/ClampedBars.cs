using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Application.Market.Bars;
using XDrishti.Application.Market.History;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Replay;

/// <summary>
/// The only way the replay reaches stored bars (SR-01, AC 3): every read is asked for a session <c>cut</c>, and a cut after the hold-out start less one day and less the embargo throws <see cref="ReplayHoldOutException"/> before anything is read. With no hold-out block fixed it
/// fails closed. A bar after the close of the cut is never returned.
/// </summary>
internal sealed class ClampedBars(IHoldOutStore holdOut, HoldOutOptions options, IBarStore minutes, IBackfillSeams seams, IBarQueries days, int readAheadSessions = 40, HoldOut.HoldOutBlockReader? block = null)
{
    private readonly int _readAheadDays = (readAheadSessions * 7 / 5) + 3;

    /// <summary>Calendar days of 1-minute bars read before the close: about 270 sessions, as the live ticket source reads.</summary>
    public const int MinuteLookbackDays = 400;

    private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    private readonly Dictionary<Guid, Window> _windows = [];
    private DateOnly? _last;
    private DateOnly? _horizon;

    /// <summary>The last session that may be read.</summary>
    public async ValueTask<DateOnly> LastReadableAsync(CancellationToken cancellationToken)
    {
        if (_last is { } known)
        {
            return known;
        }

        if (block is not null)
        {
            return (_last = block.End).Value; // the SR-03 reader after its claim: up to the end of the claimed block, never past it
        }

        var current = await holdOut.CurrentAsync(cancellationToken) ?? throw new ReplayHoldOutException();
        _last = HoldOutOptions.TrainingCut(current.Start, options.TrainingEmbargoDays); // the one shared cut formula; it moves forward only when the block in force is released (HO-01)
        return _last.Value;
    }

    /// <summary>Throws when <paramref name="cut"/> is past the clamp.</summary>
    public async ValueTask EnsureAsync(DateOnly cut, CancellationToken cancellationToken)
    {
        var last = await LastReadableAsync(cancellationToken);
        if (cut > last)
        {
            throw new ReplayHoldOutException(cut, last);
        }
    }

    /// <summary>
    /// Tells the reader the last session of the run. From then on the 1-minute bars of an instrument are read once for the run in bounded chunks (the lookback of the first cut plus <c>ReadAheadSessions</c> sessions, then the next chunk when a cut passes the end of the window; never past
    /// the last session of the run or the clamp), through <see cref="SeamAwareBars"/>, and every cut is a read-only slice of the window. The slice still passes <see cref="EnsureAsync"/> and ends at the close of the cut. The window drops what is older than the lookback of the cut, so
    /// memory stays at the lookback plus one chunk per instrument (about 5 MB per instrument: 100 instruments are about half a gigabyte) whatever the length of the run. A cut before the start of the window (never in a run, which goes forward) reads its own window again. Without it each cut reads its own window.
    /// </summary>
    public void PlanRange(DateOnly last) => _horizon = last;

    /// <summary>The 1-minute bars up to the close of <paramref name="cut"/>, as far back as <see cref="MinuteLookbackDays"/>.</summary>
    public async ValueTask<IReadOnlyList<Bar>> MinutesAsync(Guid instrumentId, DateOnly cut, CancellationToken cancellationToken)
    {
        await EnsureAsync(cut, cancellationToken);
        var to = CloseOf(cut);
        var from = to.AddDays(-MinuteLookbackDays);
        if (_horizon is not { } horizon)
        {
            return await ReadAsync(instrumentId, from, to, cancellationToken);
        }

        var cap = CloseOf(horizon < cut ? cut : horizon);
        var last = CloseOf(await LastReadableAsync(cancellationToken));
        if (cap > last)
        {
            cap = last; // the window never reaches past the clamp
        }

        var (fromEpoch, toEpoch) = (from.ToUnixTimeSeconds(), to.ToUnixTimeSeconds());
        _windows.TryGetValue(instrumentId, out var window);
        var end = to.AddDays(_readAheadDays) < cap ? to.AddDays(_readAheadDays) : cap;
        if (window is null || window.From > fromEpoch)
        {
            var bars = await ReadAsync(instrumentId, from, end, cancellationToken);
            _windows[instrumentId] = window = new Window([.. bars], fromEpoch, end.ToUnixTimeSeconds());
        }
        else if (window.To < toEpoch)
        {
            var kept = window.Bars[window.LowerBound(fromEpoch)..]; // what is older than the lookback of this cut is let go before the next chunk is added
            var more = await ReadAsync(instrumentId, DateTimeOffset.FromUnixTimeSeconds(window.To + 1), end, cancellationToken);
            _windows[instrumentId] = window = new Window([.. kept, .. more], fromEpoch, end.ToUnixTimeSeconds());
        }

        var (low, high) = (window.LowerBound(fromEpoch), window.UpperBound(toEpoch));
        return new BarSlice(window.Bars, low, high - low);
    }

    private ValueTask<IReadOnlyList<Bar>> ReadAsync(Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        SeamAwareBars.ReadAsync(minutes, seams, instrumentId, from, to, cancellationToken); // bars before the high-water seam are raw backfill and never enter a simulation, learning or planning path (ADR 0270, decision 10)

    private static DateTimeOffset CloseOf(DateOnly day) => new(day.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaOffset);

    private sealed record Window(Bar[] Bars, long From, long To)
    {
        /// <summary>The index of the first bar at or after <paramref name="epoch"/>.</summary>
        public int LowerBound(long epoch)
        {
            int lo = 0, hi = Bars.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (Bars[mid].EpochSeconds < epoch)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }

        /// <summary>The index after the last bar at or before <paramref name="epoch"/>.</summary>
        public int UpperBound(long epoch)
        {
            int lo = 0, hi = Bars.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (Bars[mid].EpochSeconds <= epoch)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }
    }

    /// <summary>The day bars of the India dates from <paramref name="from"/> up to and including <paramref name="cut"/>.</summary>
    public async ValueTask<IReadOnlyList<BarRow>> DaysAsync(Guid instrumentId, DateOnly from, DateOnly cut, CancellationToken cancellationToken)
    {
        await EnsureAsync(cut, cancellationToken);
        return await days.ReadDailyBarsAsync(instrumentId, from, cut.AddDays(1), cancellationToken);
    }
}
