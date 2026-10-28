using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The weekly estimates of the execution feedback (spec P11-02): slippage per liquidity bucket (with n, mean and median and their intervals; a bucket under <c>Live:Estimates:MinN</c> is <c>insufficient</c> with no mean) and the capacity score of the account and of
/// each bucket, over every stored execution up to the end of the week. One snapshot per week and key (insert-only; a second run writes nothing). The proposal sentence is stored beside a row and read by no one automatically: no estimate is applied to the
/// simulator, the selection or a cell. Reads stored rows only.
/// </summary>
internal sealed class ComputeLiveEstimatesHandler(ILiveExecutionStore store, ILiveEstimateSettings settings, TimeProvider time, ILogger<ComputeLiveEstimatesHandler> logger) : ICommandHandler<ComputeLiveEstimatesCommand, LiveEstimatesOutcome>
{
    public async ValueTask<Result<LiveEstimatesOutcome>> HandleAsync(ComputeLiveEstimatesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var today = IndiaTime.Today(time.GetUtcNow());
        var week = command.WeekStart ?? MondayOf(today);
        if (week.DayOfWeek != DayOfWeek.Monday)
        {
            return Result.Failure<LiveEstimatesOutcome>(Error.Validation(LiveErrors.Invalid, "weekStart must be a Monday"));
        }

        if (week > today)
        {
            return Result.Failure<LiveEstimatesOutcome>(Error.Validation(LiveErrors.Invalid, "weekStart must not be after today"));
        }

        var until = week.AddDays(6);
        var executions = (await store.ExecutionsUntilAsync(until, cancellationToken)).Select(e => e.Row).ToList();
        var classes = await store.OrderClassesUntilAsync(until, cancellationToken);
        var slippage = LiveEstimateMath.Slippage(week, executions, settings.MinN, settings.ProposalMinN);
        var capacity = LiveEstimateMath.Capacity(week, classes, settings.Edges, settings.CapacityPriorStrength, settings.CapacityMinN);
        var slippageNew = slippage.Count > 0 ? await store.InsertSlippageEstimatesAsync(slippage, cancellationToken) : 0;
        var capacityNew = await store.InsertCapacityScoresAsync(capacity, cancellationToken);
        LiveCaptureLog.Estimates(logger, executions.Count, slippage.Count, slippageNew, capacity.Count, capacityNew);
        var summary = string.Create(CultureInfo.InvariantCulture, $"week of {week:yyyy-MM-dd}: {executions.Count} executions, {slippage.Count} slippage rows ({slippageNew} new), {capacity.Count} capacity rows ({capacityNew} new)");
        return Result.Success(new LiveEstimatesOutcome(week, executions.Count, slippage.Count, slippageNew, capacity.Count, capacityNew, summary));
    }

    /// <summary>The Monday of the week a date is in.</summary>
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
}
