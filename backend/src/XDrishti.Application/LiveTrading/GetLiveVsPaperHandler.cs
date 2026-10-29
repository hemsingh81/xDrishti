using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;
using XDrishti.Domain.Market;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The live-versus-paper report (spec P11-03, ADR 0152): the same tickets on a live account and one paper account, the shortfall in R on the plan's risk per share, its interval and the non-inferiority verdict, the exclusions with their reasons, the histogram of live
/// entry slippage and the three cumulative lines. Computed on read from stored rows; reads only, nothing is written, applied or sent.
/// </summary>
internal sealed class GetLiveVsPaperHandler(
    IAccountStore accounts,
    ILivePairSource live,
    IPaperTradeSource paper,
    ILiveReportSettings settings,
    TimeProvider time,
    ILogger<GetLiveVsPaperHandler> logger) : IQueryHandler<LiveVsPaperQuery, Result<LiveVsPaperView>>
{
    private static readonly string[] Basis = ["gross", "net"];

    public async ValueTask<Result<LiveVsPaperView>> HandleAsync(LiveVsPaperQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var clock = Stopwatch.StartNew();
        var range = LiveReportRules.Range(query.From, query.To, IndiaTime.Today(time.GetUtcNow()));
        if (range.IsFailure)
        {
            return Result.Failure<LiveVsPaperView>(range.Error);
        }

        var wanted = (query.Basis ?? "gross").ToLowerInvariant();
        if (!Basis.Contains(wanted) || (query.ReferenceKind is not null && query.ReferenceKind is not (ReferenceKind.TicketLevel or ReferenceKind.PlanGrade)) || query.Page < 1 || query.Size is < 1 or > LiveReportRules.MaxPageSize)
        {
            return Result.Failure<LiveVsPaperView>(Error.Validation(LiveErrors.Invalid, "basis is gross or net, referenceKind is ticket_level or plan_grade, page starts at 1 and size is 1 to 200"));
        }

        if (await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken) is null)
        {
            return Result.Failure<LiveVsPaperView>(Error.NotFound(LiveErrors.NotFound, "No such account"));
        }

        var (from, to) = range.Value;
        var trips = await live.TripsAsync(query.AccountId, from, to, cancellationToken);
        var read = await paper.AccountsAsync(cancellationToken);
        if (!read.Available)
        {
            return Result.Success(Unavailable(wanted, trips, from, to));
        }

        var chosen = query.PaperAccountId ?? settings.PaperAccountId;
        if (chosen is { } given && !read.Accounts.Contains(given))
        {
            return Result.Failure<LiveVsPaperView>(Error.Validation(LiveErrors.Invalid, "that is not a paper account of the current epoch"));
        }

        if (chosen is null && read.Accounts.Count > 1)
        {
            return Result.Failure<LiveVsPaperView>(Error.Validation(LiveReportRules.PaperAccountRequired, "several paper accounts exist: choose one with paperAccountId or Live:Report:PaperAccountId"));
        }

        chosen ??= read.Accounts.Count == 1 ? read.Accounts[0] : null;
        PaperTradesRead paperRead = chosen is { } pa && read.EpochId is { } epoch
            ? await paper.TradesAsync(epoch, pa, [.. trips.Select(t => t.ItemId)], cancellationToken)
            : new PaperTradesRead(true, [], new Dictionary<long, string>());
        if (!paperRead.Available)
        {
            return Result.Success(Unavailable(wanted, trips, from, to));
        }

        var view = Build(query, wanted, trips, paperRead, chosen, read.EpochId, from, to);
        LiveReportLog.Served(logger, "vs_paper", view.PairsTotal, clock.ElapsedMilliseconds);
        return Result.Success(view);
    }

    private LiveVsPaperView Build(LiveVsPaperQuery query, string wanted, IReadOnlyList<LiveTrip> trips, PaperTradesRead paperRead, Guid? paperAccount, long? epoch, DateOnly from, DateOnly to)
    {
        var paperByItem = paperRead.Trades.GroupBy(t => t.ItemId).ToDictionary(g => g.Key, g => g.First());
        var excluded = new List<LiveExcludedView>();
        var pairs = new List<Pair>();
        foreach (var trip in trips)
        {
            paperByItem.TryGetValue(trip.ItemId, out var p);
            if (trip.Execution is not { } stored)
            {
                if (p is not null)
                {
                    excluded.Add(new LiveExcludedView(trip.ItemId, trip.Symbol, trip.Date, LiveReportRules.PaperOnly, $"the live ticket was not entered (outcome {trip.Outcome})"));
                }

                continue;
            }

            var e = stored.Row;
            if (p is null)
            {
                excluded.Add(new LiveExcludedView(trip.ItemId, trip.Symbol, trip.Date, LiveReportRules.LiveOnly, paperRead.SkipCodes.TryGetValue(trip.ItemId, out var code) ? $"paper skip code {code}" : "no paper entry stored"));
            }
            else if (e.ExitVwap is null || e.ExitQty != e.FillQty)
            {
                excluded.Add(new LiveExcludedView(trip.ItemId, trip.Symbol, trip.Date, LiveReportRules.LiveOpen, "no complete exit of the live position was captured in the session"));
            }
            else if (p.ExitPrice is null)
            {
                excluded.Add(new LiveExcludedView(trip.ItemId, trip.Symbol, trip.Date, LiveReportRules.PaperOpen, "the paper trade has no exit yet"));
            }
            else if (trip.Uncertain is { } doubt)
            {
                excluded.Add(new LiveExcludedView(trip.ItemId, trip.Symbol, trip.Date, LiveReportRules.LiveUncertain, "the journal doubts the round trip, so no R is read from it: " + (doubt.Length <= 200 ? doubt : doubt[..200])));
            }
            else if (e.RiskPerShare is not { } rho || PairedShortfall.Pair(e.Side == "buy", e.FillVwap, e.ExitVwap.Value, p.EntryPrice, p.ExitPrice.Value, rho) is not { } split)
            {
                excluded.Add(new LiveExcludedView(trip.ItemId, trip.Symbol, trip.Date, LiveReportRules.NoPlanLevels, "the ticket has no stop, so no risk per share"));
            }
            else
            {
                double? liveCharges = e.LiveCharges is { } lc ? (double)(lc / (rho * e.FillQty)) : null;
                double? paperCharges = p.Charges is { } pc && p.Qty > 0 ? (double)(pc / (rho * p.Qty)) : null;
                double? planR = e.PlanEntry is { } pe && e.PlanExit is { } px ? (double)((e.Side == "buy" ? 1m : -1m) * (px - pe) / rho) : null;
                pairs.Add(new Pair(trip, split, liveCharges, paperCharges, planR));
            }
        }

        // net only when every pair has both charges; otherwise the whole report is gross and says so (never mixed)
        var net = wanted == "net" && pairs.Count > 0 && pairs.All(p => p.LiveChargesR is not null && p.PaperChargesR is not null);
        var basis = net ? "net" : "gross";
        double Shortfall(Pair p) => p.Split.ShortfallR + (net ? p.LiveChargesR!.Value - p.PaperChargesR!.Value : 0);
        double LiveR(Pair p) => p.Split.LiveR - (net ? p.LiveChargesR!.Value : 0);
        double PaperR(Pair p) => p.Split.PaperR - (net ? p.PaperChargesR!.Value : 0);

        var summary = Summarise(pairs, basis, wanted, Shortfall);
        var kinds = trips.Where(t => t.Execution is not null).Select(t => t.Execution!.Row).ToList();
        var kind = query.ReferenceKind ?? (kinds.Any(k => k.ReferenceKind == ReferenceKind.TicketLevel && k.SlipTotalBps is not null) ? ReferenceKind.TicketLevel : ReferenceKind.PlanGrade);
        var observations = kinds.Where(k => k.ReferenceKind == kind && k.SlipTotalBps is not null).Select(k => k.SlipTotalBps!.Value).ToList();
        var bins = SlippageHistogram.Build(observations);
        var histogram = new LiveHistogramView(kind, observations.Count, observations.Count == 0 ? "empty" : bins is null ? "insufficient" : "value", [.. (bins ?? []).Select(b => new LiveHistogramBinView(b.Lo, b.Hi, b.Count))]);

        var ordered = pairs.OrderBy(p => p.Trip.ItemId).ToList();
        var page = ordered.Skip((query.Page - 1) * query.Size).Take(query.Size)
            .Select(p => new LivePairView(p.Trip.ItemId, p.Trip.Symbol, p.Trip.Side, p.Trip.Date, LiveR(p), PaperR(p), Shortfall(p), p.Split.EntryR, p.Split.ExitR)).ToList();
        var counts = excluded.GroupBy(x => x.Reason).ToDictionary(g => g.Key, g => g.Count());
        var evidence = LiveReportRules.Evidence(basis, pairs.Count, pairs.Count > 0 ? pairs.Min(p => p.Trip.Date) : null, pairs.Count > 0 ? pairs.Max(p => p.Trip.Date) : null, [kind], paperAccount, epoch, counts);
        var state = trips.Count == 0 && excluded.Count == 0 ? "empty" : "value";
        return new LiveVsPaperView(state, summary, page, pairs.Count, query.Page, query.Size, [.. excluded.OrderBy(x => x.PlanItemId)], histogram, Equity(ordered, LiveR, PaperR), evidence);
    }

    private LiveVsPaperSummaryView Summarise(List<Pair> pairs, string basis, string wanted, Func<Pair, double> shortfall)
    {
        var n = pairs.Count;
        var min = settings.MinPairs;
        var s = ShortfallStats.Summarise([.. pairs.Select(shortfall)], settings.NonInferiorityR, min);
        var why = $"n = {n}; an interval needs {ShortfallStats.MinimumForInterval} pairs";
        MetricView M(double? v) => s is not null && v is { } x ? LiveReportRules.Value(x) : LiveReportRules.Insufficient(why);
        MetricView Mean(Func<Pair, double?> f)
        {
            if (n < ShortfallStats.MinimumForInterval)
            {
                return LiveReportRules.Insufficient(why);
            }

            var v = pairs.Select(f).ToList();
            return v.All(x => x is not null) ? LiveReportRules.Value(v.Average(x => x!.Value)) : LiveReportRules.Unavailable("charges are not recorded on both sides for every pair");
        }

        var allCharges = n > 0 && pairs.All(p => p.LiveChargesR is not null && p.PaperChargesR is not null);
        var charges = n < ShortfallStats.MinimumForInterval
            ? LiveReportRules.Insufficient(why)
            : allCharges
                ? LiveReportRules.Value(pairs.Average(p => p.LiveChargesR!.Value - p.PaperChargesR!.Value))
                : LiveReportRules.Unavailable(wanted == "net" ? "net was asked for but charges are not recorded on both sides for every pair, so the report is gross" : "charges are not recorded on both sides for every pair");
        var verdict = s?.Verdict ?? NonInferiority.Insufficient;
        return new LiveVsPaperSummaryView(
            n, M(s?.Mean), M(s?.Sd), M(s?.Se), M(s?.Lo95), M(s?.Hi95), M(s?.UpperOneSided), M(s?.LowerOneSided),
            Mean(p => p.Split.EntryR), Mean(p => p.Split.ExitR), charges, n < min ? NonInferiority.Insufficient : verdict, settings.NonInferiorityR, min);
    }

    private static LiveEquityView Equity(List<Pair> ordered, Func<Pair, double> liveR, Func<Pair, double> paperR)
    {
        LiveEquityLineView Line(Func<Pair, double?> f, string whenMissing)
        {
            if (ordered.Count == 0)
            {
                return new LiveEquityLineView("empty", null, []);
            }

            if (ordered.Any(p => f(p) is null))
            {
                return new LiveEquityLineView("unavailable", whenMissing, []);
            }

            var points = new List<LiveEquityPointView>();
            double cumulative = 0;
            var count = 0;
            foreach (var day in ordered.GroupBy(p => p.Trip.Date).OrderBy(g => g.Key))
            {
                foreach (var p in day)
                {
                    cumulative += f(p)!.Value;
                    count++;
                }

                points.Add(new LiveEquityPointView(day.Key, cumulative, count));
            }

            return new LiveEquityLineView("value", null, points);
        }

        return new LiveEquityView(Line(p => p.PlanR, "a pair has no plan prices (the plan was not graded)"), Line(p => paperR(p), string.Empty), Line(p => liveR(p), string.Empty));
    }

    private LiveVsPaperView Unavailable(string basis, IReadOnlyList<LiveTrip> trips, DateOnly from, DateOnly to)
    {
        var no = LiveReportRules.Unavailable("the paper record could not be read");
        var line = new LiveEquityLineView("unavailable", "the paper record could not be read", []);
        return new LiveVsPaperView(
            "unavailable",
            new LiveVsPaperSummaryView(0, no, no, no, no, no, no, no, no, no, no, NonInferiority.Insufficient, settings.NonInferiorityR, settings.MinPairs),
            [], 0, 1, LiveReportRules.MaxPageSize, [], new LiveHistogramView(ReferenceKind.PlanGrade, 0, "unavailable", []), new LiveEquityView(line, line, line),
            LiveReportRules.Evidence(basis, 0, trips.Count > 0 ? from : null, trips.Count > 0 ? to : null, [], null, null, new Dictionary<string, int>()));
    }

    private sealed record Pair(LiveTrip Trip, PairedShortfall Split, double? LiveChargesR, double? PaperChargesR, double? PlanR);
}
