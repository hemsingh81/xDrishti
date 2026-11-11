using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Context;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Reporting;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Reporting;

/// <summary>
/// The performance profile of one basket (BK-07, ADR 0262): the membership by date, the stored daily bars of the members and of the index (one read each), the regimes of the window and the median split of the basket's strategy trades, composed into the pure
/// <see cref="BasketAnalytics"/> input and mapped to the wire view. Reads only. The answer is cached under a key that holds everything it depends on (the basket, its parameter hash and membership version, the window, the index and the newest stored bar), so a second
/// call reads no bars and returns the same bytes, and a new bar is a new key.
/// </summary>
internal sealed class GetBasketProfileHandler(
    ProfileWindowResolver windows,
    BasketMembershipLoader memberships,
    IBarQueries bars,
    IContextFigures context,
    ReportLoader trades,
    IReportTradeSource runs,
    IProfileCache cache,
    BasketProfileSettings settings,
    ReportBenchmarkOptions benchmarkSettings,
    CostStressOptions costStress,
    TimeProvider time,
    ILogger<GetBasketProfileHandler> logger) : IQueryHandler<GetBasketProfileQuery, Result<BasketProfileView>>
{
    public async ValueTask<Result<BasketProfileView>> HandleAsync(GetBasketProfileQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = time.GetTimestamp();
        if (!Guid.TryParse(query.Id?.Trim(), out var id))
        {
            return Result.Failure<BasketProfileView>(ReportErrors.Invalid("id is a basket id."));
        }

        var window = await windows.ResolveAsync(query.From, query.To, query.Index, cancellationToken);
        if (window.IsFailure)
        {
            return Result.Failure<BasketProfileView>(window.Error);
        }

        var membership = await memberships.LoadAsync(id, window.Value, query.MembersAsOf, cancellationToken);
        if (membership.IsFailure)
        {
            return Result.Failure<BasketProfileView>(membership.Error);
        }

        var win = window.Value;
        var basket = membership.Value;
        var key = await KeyAsync(basket, win, cancellationToken);
        if (cache.Get<BasketProfileView>(key) is { } hit)
        {
            var hitMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            ReportLog.BasketProfile(logger, basket.Instruments.Count, 0, true, hitMs);
            return Result.Success(hit);
        }

        var reader = new ProfileBars(bars);
        var view = await BuildAsync(basket, win, reader, cancellationToken);
        cache.Set(key, view);
        var elapsed = (long)time.GetElapsedTime(started).TotalMilliseconds;
        ReportLog.BasketProfile(logger, basket.Instruments.Count, reader.Reads, false, elapsed);
        return Result.Success(view);
    }

    private async ValueTask<string> KeyAsync(BasketMembership basket, ResolvedWindow win, CancellationToken cancellationToken)
    {
        // Shortcut (ADR 0262): the key does not see a backfill of old bars or a rewrite of the stored market context; those show when the index gets a newer bar or the cache is emptied (a restart).
        // the strategy panel reads the default run set, so a new run changes the answer as a new bar does
        var chosen = RunSelection.Default(await runs.ListRunsAsync(cancellationToken)).Chosen.Select(r => r.RunKey[..Math.Min(12, r.RunKey.Length)]).Order(StringComparer.Ordinal);
        return string.Join('|', "profile", basket.Id.ToString("D"), basket.ParamsHash ?? "-", basket.Version, basket.AsOf.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            win.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), win.To.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            win.Index?.Id.ToString("D") ?? "-", win.Newest.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), string.Join(',', chosen));
    }

    private async ValueTask<BasketProfileView> BuildAsync(BasketMembership basket, ResolvedWindow win, ProfileBars reader, CancellationToken cancellationToken)
    {
        var options = settings.ToOptions(benchmarkSettings.DiscontinuityThreshold, (double)costStress.CapacityDailyShare);
        var members = new List<MemberSeries>(basket.Instruments.Count);
        foreach (var m in basket.Instruments)
        {
            var (closes, volumes) = await reader.ReadAsync(m.Id, win.From, win.To, cancellationToken);
            members.Add(new MemberSeries(m.Symbol, closes, volumes, m.IsFno, m.IsAsmGsm));
        }

        IReadOnlyList<DailyClose> benchmark = win.Index is { } index ? (await reader.ReadAsync(index.Id, win.From, win.To, cancellationToken)).Closes : [];
        var regimes = await RegimesAsync(members, benchmark, win, cancellationToken);
        var input = new BasketProfileInput(members, basket.At, basket.SnapshotDates, benchmark, regimes, new ProfileWindow(win.From, win.To));
        var profile = BasketAnalytics.Profile(input, options);

        var asOf = profile.AsOf;
        var current = asOf is { } d ? members.Where(m => basket.At(d).Contains(m.Symbol) && HasBars(m, win.To)).ToList() : [];
        var returns = current.ToDictionary(m => m.Symbol, m => ReturnSeries.FromCloses(m.Closes.Select(c => (c.Date, c.Close)), asOf!.Value), StringComparer.Ordinal);
        var sectors = basket.Instruments.ToDictionary(i => i.Symbol, i => i.Sector, StringComparer.Ordinal);
        var series = asOf is { } last ? BasketBreadth.SeriesAbove20(current, last, Math.Max(1, profile.Index.Points.Count), options) : [];
        var hold = await HoldOutAsync(basket.Id, cancellationToken);
        var excluded = Exclusions(basket, members, profile, win);
        var noBenchmark = BasketProfileMapper.NoBenchmark(win, benchmark.Count > 0);
        var unavailable = new List<string>();
        if (noBenchmark is not null)
        {
            unavailable.Add("benchmark index: " + noBenchmark);
        }

        if (profile.Regime.Status.State == MetricState.Unavailable)
        {
            unavailable.Add("regime: " + profile.Regime.Status.Reason);
        }

        return new BasketProfileView(
            new ProfileBasketView(basket.Id, basket.Name, basket.Kind, basket.ParamsHash),
            win.View,
            basket.AsOf,
            basket.Basis,
            profile.FirstDatedMembership,
            new ProfileMembersView(basket.Instruments.Count, basket.Instruments.Count - excluded.Count, excluded),
            new ProfileIndexView(win.Index?.Id, win.Index?.Symbol, win.IndexIsDefault, win.Index is null ? null : win.IndexName),
            ReportViews.Metric(profile.HoldReturnPct),
            BasketProfileMapper.Series(profile, benchmark),
            BasketProfileMapper.Risk(profile, options, noBenchmark),
            new IndexDifferenceView(ReportViews.Metric(profile.HoldReturnPct), BasketProfileMapper.Against(profile.IndexReturnPct, noBenchmark), BasketProfileMapper.Against(profile.DifferenceToIndex, noBenchmark)),
            BasketProfileMapper.Breadth(profile.Breadth, series),
            new ProfileDispersionView(ReportViews.Metric(profile.Dispersion.Stdev), ReportViews.Metric(profile.Dispersion.MeanStdev), ReportViews.Metric(profile.Dispersion.Iqr), profile.Dispersion.Voters),
            new ProfileCorrelationView(ReportViews.Metric(profile.Correlation.AveragePairwise), ReportViews.Metric(profile.Correlation.EffectiveBets), profile.Correlation.Members, profile.Correlation.Pairs, profile.Correlation.Sampled,
                BasketProfileMapper.Matrix(returns, profile, sectors, options)),
            BasketProfileMapper.Contribution(profile.Contribution),
            BasketProfileMapper.Regime(profile.Regime, options.MinDaysPerRegime),
            new ProfileTurnoverView(ReportViews.Metric(profile.Turnover.Turnover), profile.Turnover.Adds, profile.Turnover.Removes),
            BasketProfileMapper.Capacity(profile.Capacity, options.CapacityPct),
            hold,
            [.. profile.RelativeStrength.Returns.Select(r => new RelativeStrengthReturnView(r.Window, BasketProfileMapper.Against(r.Return, noBenchmark)))],
            new BenchmarkEvidenceView(BasketProfileMapper.Basis, basket.Instruments.Count - excluded.Count, win.View, Limits(basket, profile, options), unavailable));
    }

    private static bool HasBars(MemberSeries m, DateOnly to) => m.Closes.Any(c => c.Date <= to && double.IsFinite(c.Close) && c.Close > 0);

    private static List<ProfileExclusionView> Exclusions(BasketMembership basket, List<MemberSeries> members, BasketProfile profile, ResolvedWindow win)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in profile.Contribution.Excluded)
        {
            result[e.Symbol] = e.Reason;
        }

        foreach (var left in profile.Index.LeftOut)
        {
            result.TryAdd(left.Symbol, left.Reason);
        }

        foreach (var m in members)
        {
            if (!HasBars(m, win.To))
            {
                result.TryAdd(m.Symbol, BuyAndHold.NoBars);
            }
        }

        return [.. result.Where(kv => basket.Instruments.Any(i => i.Symbol == kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new ProfileExclusionView(kv.Key, kv.Value))];
    }

    private static List<string> Limits(BasketMembership basket, BasketProfile profile, BasketProfileOptions options)
    {
        var limits = new List<string>
        {
            "Equal weight, rebalanced every day, price return only: dividends are not included. The weights of a day are those of the previous session's membership.",
            basket.Basis == MemberTurnover.SingleSnapshot
                ? $"Members as of {basket.AsOf:yyyy-MM-dd}, not as of each trading day; survivorship applies (ISS-22)."
                : $"Membership is dated from {profile.FirstDatedMembership:yyyy-MM-dd}; before that date the first known members apply (ISS-22).",
            $"An instrument-day with a one-session move above {options.DiscontinuityThreshold:P0} is left out, named and never repaired; a corporate action not adjusted in the stored bars looks the same.",
            "Beta, alpha, tracking error and the information ratio are against the chosen index with the risk-free rate ignored; the effective number of bets is a heuristic, not a measured count.",
            "The difference to the index is passive (hold against hold) and is not a trading result; it is never to be read as alpha.",
            $"Capacity is {options.CapacityPct * 100:0.##}% of each member's ADV20: the one capacity share of the system (Confidence:CostStress:CapacityDailyShare), not a setting of this screen.",
            "The profile describes the past; no figure here predicts.",
        };
        var unknown = basket.Instruments.Where(i => !i.FlagsKnown).Select(i => i.Symbol).ToList();
        if (unknown.Count > 0)
        {
            limits.Insert(limits.Count - 1, $"F&O and ASM/GSM flags are not known for {string.Join(", ", unknown)} (members added and removed inside the window); they count in neither share.");
        }

        return limits;
    }

    private async ValueTask<IReadOnlyDictionary<DateOnly, int?>> RegimesAsync(List<MemberSeries> members, IReadOnlyList<DailyClose> benchmark, ResolvedWindow win, CancellationToken cancellationToken)
    {
        var sessions = new HashSet<DateOnly>();
        foreach (var c in benchmark.Count > 0 ? benchmark : members.SelectMany(m => m.Closes).ToList())
        {
            if (c.Date >= win.From && c.Date <= win.To)
            {
                sessions.Add(c.Date);
            }
        }

        // the label of a session is the regime of the context day read for the next date; it counts only when that very session is the day read
        var reads = sessions.Count == 0 ? new Dictionary<DateOnly, ContextRead>() : new Dictionary<DateOnly, ContextRead>(await context.AsOfManyAsync([.. sessions.Select(s => s.AddDays(1))], cancellationToken));
        var codes = new Dictionary<DateOnly, int?>();
        foreach (var s in sessions)
        {
            var label = reads.TryGetValue(s.AddDays(1), out var read) ? RegimeLabel.Of(s.AddDays(1), read) : null;
            if (label is { IsAvailable: true, ContextAsOf: { } day } && day == s)
            {
                codes[s] = label.Code;
            }
        }

        return codes;
    }

    private async ValueTask<ProfileHoldOutView> HoldOutAsync(Guid basketId, CancellationToken cancellationToken)
    {
        var query = ReportQuery.Default with { Filters = ReportFilters.None with { Baskets = [basketId] } };
        var loaded = await trades.LoadAsync(query, "basket_profile", cancellationToken);
        if (loaded.IsFailure)
        {
            return BasketProfileMapper.HoldOutUnavailable(loaded.Error.Message);
        }

        using var report = loaded.Value;
        var lines = report.Population.Trades(0, report.Population.TradeCount);
        return BasketProfileMapper.HoldOut(HoldOutStability.Compute(lines.Select(l => new HoldOutTrade(l.Strategy, l.ExitDate, l.R))), lines.Count);
    }
}
