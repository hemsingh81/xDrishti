using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Application.Market.Actions;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Planning;

/// <summary>The daily chart of one ticket (spec P8-14). Read only.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ItemId">The ticket.</param>
/// <param name="Bars">10 to 250 (default 60).</param>
public sealed record GetPlanItemChartQuery(long PlanId, long ItemId, int Bars) : IQuery<Result<TicketChartView>>;

/// <summary>
/// The last daily bars up to the plan's last closed session, adjusted by the corporate actions applied as of that session (a later split does not change what the planner saw), and the ticket's own numbers and texts.
/// Bars after the session are never read: a plan chart has no hindsight. A symbol that is not exactly one active share gets no bars.
/// </summary>
internal sealed class GetPlanItemChartHandler(
    IPlanStore store,
    IInstrumentQueries instruments,
    IBarQueries bars,
    IAdjustmentFactors adjustments,
    ITradingCalendar calendar,
    ILogger<GetPlanItemChartHandler> logger) : IQueryHandler<GetPlanItemChartQuery, Result<TicketChartView>>
{
    public const int MinBars = 10;

    public const int MaxBars = 250;

    public async ValueTask<Result<TicketChartView>> HandleAsync(GetPlanItemChartQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Bars is < MinBars or > MaxBars)
        {
            return Result.Failure<TicketChartView>(PlanErrors.Invalid([new FieldError("bars", $"Use {MinBars} to {MaxBars}.")]));
        }

        var watch = Stopwatch.StartNew();
        var plan = await store.FindAsync(query.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<TicketChartView>(PlanErrors.NotFound(query.PlanId));
        }

        var row = await store.FindItemAsync(query.ItemId, cancellationToken);
        if (row is null || row.PlanId != query.PlanId)
        {
            return Result.Failure<TicketChartView>(PlanErrors.ItemNotFound(query.ItemId));
        }

        var item = row.Item;
        var session = item.Levels?.Session ?? await SessionBeforeAsync(plan.Plan.ForDate, cancellationToken);
        var levels = item.Levels is { } l
            ? new TicketChartLevels(l.EntryRef, l.Stop, l.Anchor?.Code(), l.Target, l.RewardRisk, l.Session, TicketLevelsView.BasisText, "ticket")
            : new TicketChartLevels(null, null, null, null, null, null, null, "none");
        var rules = new TicketChartRules(item.Trigger, item.Stop, item.Target, item.Validity);

        var found = (await instruments.FindForResolveAsync([item.Symbol.ToUpperInvariant()], [], cancellationToken))
            .Where(c => !c.Retired && c.Type == InstrumentTypeCodes.Share && string.Equals(c.Symbol, item.Symbol, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (found.Count != 1)
        {
            var resolution = found.Count == 0 ? "not_found" : "ambiguous";
            return Result.Success(new TicketChartView(item.Symbol, resolution, null, session, "1D", true, 0, [], levels, rules, Limits(resolution)));
        }

        var id = found[0].Id;
        var from = session.AddDays(-(query.Bars * 7 / 4 + 10));
        var rows = await bars.ReadDailyBarsAsync(id, from, session.AddDays(1), cancellationToken);
        var factors = await adjustments.GetAsync(id, cancellationToken);
        var asOf = AdjustmentFactors.From(factors.Actions.Where(a => a.ExDate <= session));
        var result = rows
            .Select(r => (Date: IndiaTime.Today(r.Ts), Bar: r))
            .Where(x => x.Date <= session)
            .TakeLast(query.Bars)
            .Select(x =>
            {
                var f = asOf.FactorAt(x.Date);
                return new TicketChartBar(x.Date, AdjustmentFactors.Price(x.Bar.Open, f), AdjustmentFactors.Price(x.Bar.High, f), AdjustmentFactors.Price(x.Bar.Low, f), AdjustmentFactors.Price(x.Bar.Close, f), AdjustmentFactors.Volume(x.Bar.Volume, f));
            })
            .ToList();
        ChartLog.Read(logger, query.PlanId, query.ItemId, result.Count, watch.ElapsedMilliseconds);
        return Result.Success(new TicketChartView(item.Symbol, "exact", id, session, "1D", true, result.Count, result, levels, rules, Limits("exact")));
    }

    // The trading day before the plan date (the planner's last closed session).
    private async ValueTask<DateOnly> SessionBeforeAsync(DateOnly forDate, CancellationToken cancellationToken)
    {
        var cal = await calendar.GetAsync(cancellationToken);
        for (var back = 1; back <= 14; back++)
        {
            var day = forDate.AddDays(-back);
            if (cal.IsTradingDay(day))
            {
                return day;
            }
        }

        return forDate.AddDays(-1);
    }

    private static string[] Limits(string resolution) =>
        resolution == "exact"
            ? ["Daily bars up to the planning session, adjusted as of that session; nothing after it is shown.", "A level is indicative from the close, not tomorrow's price. No order is placed."]
            : [resolution == "ambiguous" ? "The symbol matches more than one active instrument, so no chart is shown." : "The symbol matches no active instrument, so no chart is shown."];
}
