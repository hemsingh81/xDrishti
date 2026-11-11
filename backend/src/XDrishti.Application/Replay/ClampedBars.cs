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
internal sealed class ClampedBars(IHoldOutStore holdOut, HoldOutOptions options, IBarStore minutes, IBackfillSeams seams, IBarQueries days)
{
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

        var block = await holdOut.CurrentAsync(cancellationToken) ?? throw new ReplayHoldOutException();
        _last = block.Start.AddDays(-1 - options.TrainingEmbargoDays);
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
    /// Tells the reader the last session of the run. From then on the 1-minute bars of an instrument are read once for the whole run (the window of the first cut up to the close of that session, never past the clamp) and every cut is a slice of it
    /// (the rolling window of the non-functional section of SR-01); the slice still passes <see cref="EnsureAsync"/> and ends at the close of the cut, so nothing after it is returned. Without it each cut reads its own window.
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

        var end = CloseOf(horizon < cut ? cut : horizon); // the clamp holds for it: the horizon is the end of the range, which the driver refuses past the clamp, and a cut was checked above
        var last = await LastReadableAsync(cancellationToken);
        if (end > CloseOf(last))
        {
            end = CloseOf(last);
        }

        if (!_windows.TryGetValue(instrumentId, out var window) || window.From > from.ToUnixTimeSeconds() || window.To < to.ToUnixTimeSeconds())
        {
            var bars = await ReadAsync(instrumentId, from, end, cancellationToken);
            _windows[instrumentId] = window = new Window([.. bars], from.ToUnixTimeSeconds(), end.ToUnixTimeSeconds());
        }

        var (low, high) = (window.LowerBound(from.ToUnixTimeSeconds()), window.UpperBound(to.ToUnixTimeSeconds()));
        if (low > window.Bars.Length / 2 && low > 4096)
        {
            window = new Window(window.Bars[low..], from.ToUnixTimeSeconds(), window.To); // the window rolls: bars older than the lookback of this cut are never asked for again, so they are let go
            _windows[instrumentId] = window;
            (low, high) = (0, high - low);
        }

        return new ArraySegment<Bar>(window.Bars, low, high - low);
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
