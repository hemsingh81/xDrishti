using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.History;

/// <summary>
/// The one read of stored 1-minute bars for every simulation, learning and report path (CF-04 Part B, ADR 0270 decision 10): bars before an instrument's high-water seam are Dhan's raw backfill and never enter any of them. The pipeline, the quality checks and the
/// repair read <see cref="IBarStore"/> directly because they need every bar. The cut is the <b>high-water mark</b> of the seam (the latest seam ever recorded), so a seam that later moves earlier cannot admit raw Dhan bars.
/// </summary>
public static class SeamAwareBars
{
    public static async ValueTask<IReadOnlyList<Bar>> ReadAsync(IBarStore bars, IBackfillSeams seams, Guid instrumentId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(seams);
        var raw = await bars.ReadAsync(instrumentId, from, to, cancellationToken);
        return await CutAsync(seams, instrumentId, raw, cancellationToken);
    }

    /// <summary>Drops the bars before the instrument's high-water seam from an already read list.</summary>
    public static async ValueTask<IReadOnlyList<Bar>> CutAsync(IBackfillSeams seams, Guid instrumentId, IReadOnlyList<Bar> raw, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seams);
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Count == 0 || await seams.SeamOfAsync(instrumentId, cancellationToken) is not { } seam)
        {
            return raw;
        }

        var cut = seam.ToUnixTimeSeconds();
        return [.. raw.Where(b => b.EpochSeconds >= cut)];
    }
}
