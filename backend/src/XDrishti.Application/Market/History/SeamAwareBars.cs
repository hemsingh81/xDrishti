using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.History;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.History;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Market.History;

/// <summary>The bars a run feeds for one instrument, the factors that go with them and what the admission rule said.</summary>
public sealed record RunBars(IReadOnlyList<Bar> Bars, AdjustmentFactors Factors, PreSeamDecision Decision);

/// <summary>
/// The one read of stored 1-minute bars for every simulation, learning and report path (CF-04 Part B, ADR 0270 decision 10): bars before an instrument's high-water seam are Dhan's raw backfill and, but for the three readers that use <see cref="ReadForRunAsync"/>, never enter any of them.
/// The pipeline, the quality checks and the repair read <see cref="IBarStore"/> directly because they need every bar. The cut is the <b>high-water mark</b> of the seam (the latest seam ever recorded), so a seam that later moves earlier cannot admit raw Dhan bars, and it is
/// <b>date-based</b> (CF-04d decision 8): India midnight of the seam date, so the seam day is a pure CSV day and the cut agrees with the writer's guard and the daily readers.
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

    /// <summary>Drops the bars before the India midnight of the instrument's high-water seam date from an already read list.</summary>
    public static async ValueTask<IReadOnlyList<Bar>> CutAsync(IBackfillSeams seams, Guid instrumentId, IReadOnlyList<Bar> raw, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seams);
        ArgumentNullException.ThrowIfNull(raw);
        if (raw.Count == 0 || await seams.SeamOfAsync(instrumentId, cancellationToken) is not { } seam)
        {
            return raw;
        }

        var cut = new DateTimeOffset(IndiaTime.Today(seam).ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUnixTimeSeconds();
        return [.. raw.Where(b => b.EpochSeconds >= cut)];
    }

    /// <summary>
    /// The read of the three readers that may admit pre-seam bars (CF-04d decision 6: the run reader, the feature extraction and the trade replay). An instrument that passes <see cref="SeamVerification"/> gets its raw bars from <paramref name="from"/> and the
    /// source-aware factors; every other instrument gets <see cref="ReadAsync"/>'s cut bars and the plain factors, as before. Every other reader keeps <see cref="ReadAsync"/>.
    /// </summary>
    public static async ValueTask<RunBars> ReadForRunAsync(IBarStore bars, IBackfillSeams seams, Guid instrumentId, AdjustmentFactors factors, DateOnly? csvAdjustedThrough, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(seams);
        ArgumentNullException.ThrowIfNull(factors);
        var decision = await PreSeamAdmission.DecideAsync(seams, instrumentId, factors, csvAdjustedThrough, false, cancellationToken);
        var raw = await bars.ReadAsync(instrumentId, from, to, cancellationToken);
        if (decision.Admitted is { } admitted)
        {
            return new RunBars(raw, factors.SourceAware(admitted.SeamDate, csvAdjustedThrough), decision);
        }

        return new RunBars(await CutAsync(seams, instrumentId, raw, cancellationToken), factors, decision);
    }

    /// <summary>
    /// The read of a reader that follows a <b>stored run</b> (the trade cards, the feature extraction, the trade replay; CF-04d): the run's record decides, not today's switch. An instrument the run did not admit (<paramref name="recorded"/> null) is read as <see cref="ReadAsync"/> with the plain
    /// factors, whatever the switch says now. An instrument it admitted must still be admitted on the same seam date and basis; if not, the answer is null (stale) and the caller skips or refuses.
    /// </summary>
    public static async ValueTask<RunBars?> ReadRecordedAsync(IBarStore bars, IBackfillSeams seams, Guid instrumentId, AdjustmentFactors factors, DateOnly? csvAdjustedThrough, PreSeamRecord? recorded, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(seams);
        ArgumentNullException.ThrowIfNull(factors);
        if (recorded is null)
        {
            return new RunBars(await ReadAsync(bars, seams, instrumentId, from, to, cancellationToken), factors, PreSeamDecision.NotApplicable("not_admitted_by_the_run"));
        }

        var read = await ReadForRunAsync(bars, seams, instrumentId, factors, csvAdjustedThrough, from, to, cancellationToken);
        return read.Decision.Admitted == recorded ? read : null;
    }
}
