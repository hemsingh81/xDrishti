using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>A day's measured loss with the health it was read under.</summary>
internal sealed record DayMeasure(DateOnly Date, string Health, DayLoss Loss);

/// <summary>
/// Measures the loss of one session from the stored fills (spec P11-04 criterion 10, ADR 0151): the fills of the day are paired first in, first out per symbol (<see cref="DayLoss.Measure"/>), open lots are marked at the newest stored one-minute close
/// not after the moment of the measure. Unavailable unless the day's capture is healthy: a capture gap means fills may be missing, and a missing loss is never "no loss". Positions carried in from earlier days and charges are not in the figure; the screen says so.
/// Reads stored data only.
/// </summary>
internal sealed class LiveLossReader(ILiveCaptureStore capture, ILiveExecutionMarket market, TimeProvider time)
{
    public async ValueTask<DayMeasure> MeasureAsync(Guid accountId, DateOnly date, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var runs = await capture.RunsAsync(accountId, date, cancellationToken);
        var health = CaptureHealth.Evaluate(runs, date, now);
        if (health != CaptureHealth.Healthy)
        {
            return new DayMeasure(date, health, DayLoss.Unavailable);
        }

        var fills = await capture.FillsAsync(accountId, date, cancellationToken);
        if (fills.Count == 0)
        {
            return new DayMeasure(date, health, new DayLoss(MeasureStates.Value, 0m, 0m, 0, 0));
        }

        var end = new DateTimeOffset(date.ToDateTime(CaptureHealth.WindowEnd), IndiaTime.Offset);
        var notAfter = now < end ? now : end;
        var marks = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        foreach (var symbol in fills.Select(f => f.Symbol).Distinct(StringComparer.Ordinal))
        {
            marks[symbol] = await market.LastPriceAsync(symbol, date, notAfter, cancellationToken);
        }

        return new DayMeasure(date, health, DayLoss.Measure(fills, s => marks.GetValueOrDefault(s)));
    }
}
