using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;
using XDrishti.Domain.Research;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The execution gap of a live account by strategy or month (spec P11-03 criterion 8, ADR 0152): plan R - missed triggers - missed (cause unknown) - entry slippage - exit difference - charges = actual R, summed over the tickets whose plan graded, each ticket
/// counted once. The evidence dates come from the counted tickets only. The total is built from the same tickets, so a gross total can sit over net groups (a group with recorded charges is net, the total is gross as soon as one counted trip lacks them). A ticket whose outcome is unknown, whose round trip is open, or whose plan has no R is left out and listed (so the identity holds on the rest). Reads stored rows only.
/// </summary>
internal sealed class GetExecutionGapHandler(IAccountStore accounts, ILivePairSource live, TimeProvider time, ILogger<GetExecutionGapHandler> logger) : IQueryHandler<ExecutionGapQuery, Result<ExecutionGapView>>
{
    private const int MinForMean = 5;

    public async ValueTask<Result<ExecutionGapView>> HandleAsync(ExecutionGapQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var clock = Stopwatch.StartNew();
        var range = LiveReportRules.Range(query.From, query.To, IndiaTime.Today(time.GetUtcNow()));
        if (range.IsFailure)
        {
            return Result.Failure<ExecutionGapView>(range.Error);
        }

        var by = (query.GroupBy ?? "strategy").ToLowerInvariant();
        if (by is not ("strategy" or "month"))
        {
            return Result.Failure<ExecutionGapView>(Error.Validation(LiveErrors.Invalid, "groupBy is strategy or month"));
        }

        if (await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken) is null)
        {
            return Result.Failure<ExecutionGapView>(Error.NotFound(LiveErrors.NotFound, "No such account"));
        }

        var (from, to) = range.Value;
        var trips = await live.TripsAsync(query.AccountId, from, to, cancellationToken);
        var counted = new List<GapTicket>();
        var countedDates = new List<DateOnly>();
        var excluded = new List<LiveGapExcludedView>();
        foreach (var trip in trips)
        {
            var before = counted.Count;
            var key = by == "month" ? trip.Date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) : trip.Strategy;
            void Leave(string reason) => excluded.Add(new LiveGapExcludedView(trip.ItemId, trip.Symbol, trip.Date, key, reason));
            switch (trip.Outcome)
            {
                case OutcomeCodes.NotTriggered:
                    break; // the plan did not trigger: nothing was lost on either side
                case OutcomeCodes.Unknown:
                    Leave(LiveReportRules.Unknown);
                    break;
                case OutcomeCodes.MissedNoOrder or OutcomeCodes.MissedUnfilled or OutcomeCodes.Rejected or OutcomeCodes.MissedUnknown:
                    if (trip.GradeR is not { } r)
                    {
                        Leave(LiveReportRules.PlanNotGraded);
                    }
                    else
                    {
                        counted.Add(new GapTicket(key, trip.Outcome == OutcomeCodes.MissedUnknown ? GapKind.MissedUnknown : GapKind.Missed, r, 0, 0, null, 0, null));
                    }

                    break;
                default:
                    Trip(trip, key, counted, Leave);
                    break;
            }

            if (counted.Count > before)
            {
                countedDates.Add(trip.Date);
            }
        }

        var groups = counted.GroupBy(t => t.Group).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => View(ExecutionGapBuilder.Sum(g.Key, [.. g]), excluded.Count(x => x.Group == g.Key))).ToList();
        foreach (var only in excluded.Select(x => x.Group).Distinct().Where(k => groups.All(g => g.Key != k)))
        {
            groups.Add(View(null, excluded.Count(x => x.Group == only), only));
        }

        groups = [.. groups.OrderBy(g => g.Key, StringComparer.Ordinal)];
        var total = View(ExecutionGapBuilder.Sum("total", counted), excluded.Count, "total");
        var counts = excluded.GroupBy(x => x.Reason).ToDictionary(g => g.Key, g => g.Count());
        var evidence = LiveReportRules.Evidence(total.Basis, counted.Count, countedDates.Count > 0 ? countedDates.Min() : null, countedDates.Count > 0 ? countedDates.Max() : null, ReferenceKind.All.Where(k => k != ReferenceKind.Unavailable).ToList(), null, null, counts);
        LiveReportLog.Served(logger, "execution_gap", counted.Count, clock.ElapsedMilliseconds);
        return Result.Success(new ExecutionGapView(counted.Count == 0 && excluded.Count == 0 ? "empty" : "value", by, groups, total, [.. excluded.OrderBy(x => x.PlanItemId)], evidence));
    }

    private static void Trip(LiveTrip trip, string key, List<GapTicket> counted, Action<string> leave)
    {
        if (trip.Execution is not { } stored)
        {
            leave(LiveReportRules.Unknown);
            return;
        }

        var e = stored.Row;
        if (e.ExitVwap is null || e.ExitQty != e.FillQty)
        {
            leave(LiveReportRules.LiveOpen);
            return;
        }

        if (e.PlanEntry is null || e.PlanExit is null)
        {
            leave(LiveReportRules.PlanNotGraded);
            return;
        }

        var w = ExecutionWaterfall.Build(e.Side == "buy" ? Side.Buy : Side.Sell, e.PlanEntry.Value, e.PlanExit.Value, e.PlanStop, e.FillVwap, e.ExitVwap.Value, e.FillQty, e.LiveCharges, e.PlanChargesR);
        if (w.Rows[0].R is not { } plan || w.Rows[1].R is not { } entry || w.Rows[2].R is not { } exit || w.Rows[3].R is not { } gross)
        {
            leave(LiveReportRules.NoPlanLevels);
            return;
        }

        var net = w.Rows.Count > 5;
        counted.Add(new GapTicket(key, GapKind.Trip, plan, entry, exit, net ? w.Rows[4].R : null, gross, net ? w.Rows[5].R : null));
    }

    private static LiveGapGroupView View(GapSums? s, int excluded, string? key = null)
    {
        if (s is null || s.N == 0)
        {
            var no = LiveReportRules.Unavailable("no ticket of the group could be counted");
            return new LiveGapGroupView(key ?? s?.Group ?? string.Empty, 0, "unavailable", "gross", true, excluded, Rows(no, no, no, no, no, no, no), no);
        }

        MetricView V(double v) => LiveReportRules.Value(v);
        var charges = s.Charges is { } c ? V(c) : s.Trips == 0 ? V(0) : LiveReportRules.Unavailable("charges are not recorded for every round trip of the group, so the result is gross");
        var mean = s.N < MinForMean ? LiveReportRules.Insufficient($"n = {s.N}; a per-ticket mean needs {MinForMean} tickets") : V(s.Result / s.N);
        return new LiveGapGroupView(s.Group, s.N, "value", s.Basis, s.Balanced, excluded, Rows(V(s.Plan), V(-s.Missed), V(-s.MissedUnknown), V(-s.Entry), V(-s.Exit), charges is { Number: { } n } ? V(-n) : charges, V(s.Result)), mean);
    }

    private static List<LiveGapRowView> Rows(MetricView plan, MetricView missed, MetricView unknown, MetricView entry, MetricView exit, MetricView charges, MetricView result) =>
    [
        new("Plan R", plan),
        new("Missed triggers", missed),
        new("Missed (cause unknown)", unknown),
        new("Entry slippage", entry),
        new("Exit difference", exit),
        new("Charges difference", charges),
        new("Result", result),
        new("Overrides", LiveReportRules.Unavailable("measured by the decisions report of P8-04")),
    ];
}
