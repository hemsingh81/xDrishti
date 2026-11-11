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

    private DateOnly? _last;

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

    /// <summary>The 1-minute bars up to the close of <paramref name="cut"/>.</summary>
    public async ValueTask<IReadOnlyList<Bar>> MinutesAsync(Guid instrumentId, DateOnly cut, CancellationToken cancellationToken)
    {
        await EnsureAsync(cut, cancellationToken);
        var to = new DateTimeOffset(cut.AddDays(1).ToDateTime(TimeOnly.MinValue), IndiaOffset);
        return await SeamAwareBars.ReadAsync(minutes, seams, instrumentId, to.AddDays(-MinuteLookbackDays), to, cancellationToken); // bars before the high-water seam are raw backfill and never enter a simulation, learning or planning path (ADR 0270, decision 10)
    }

    /// <summary>The day bars of the India dates from <paramref name="from"/> up to and including <paramref name="cut"/>.</summary>
    public async ValueTask<IReadOnlyList<BarRow>> DaysAsync(Guid instrumentId, DateOnly from, DateOnly cut, CancellationToken cancellationToken)
    {
        await EnsureAsync(cut, cancellationToken);
        return await days.ReadDailyBarsAsync(instrumentId, from, cut.AddDays(1), cancellationToken);
    }
}
