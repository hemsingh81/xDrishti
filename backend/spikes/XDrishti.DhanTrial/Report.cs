using System.Globalization;
using System.Text;

namespace XDrishti.DhanTrial;

internal static class Stats
{
    public static double Percentile(IReadOnlyList<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        var rank = (int)Math.Ceiling(p * sorted.Length) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Length - 1)];
    }

    public static IReadOnlyList<EndpointStats> ByEndpoint(IEnumerable<CallRecord> calls)
        => [.. calls.GroupBy(c => c.Endpoint).OrderBy(g => g.Key).Select(g =>
        {
            var ms = g.Select(c => c.Millis).ToArray();
            var errors = g.Where(c => c.ErrorCode is not null).GroupBy(c => c.ErrorCode!).ToDictionary(e => e.Key, e => e.Count());
            return new EndpointStats(g.Key, g.Count(), Percentile(ms, 0.5), Percentile(ms, 0.95), ms.Max(), errors);
        })];

    public static EodEstimate Estimate(int instruments, double oneDayMs, double dailyMs, double requestsPerSecond, int backfillChunks)
    {
        var sequential = instruments * (oneDayMs + dailyMs) / 1000.0;
        var rateLimited = instruments * 2 / requestsPerSecond;
        var backfillCalls = backfillChunks + 1;
        return new EodEstimate(instruments, sequential, Math.Max(rateLimited, sequential / 8), backfillCalls, instruments * backfillCalls / requestsPerSecond);
    }
}

/// <summary>Renders the trial's Markdown report. Every string comes from already-redacted sources; the builder redacts once more at the end.</summary>
internal static class ReportBuilder
{
    public static string Build(TrialResults r, Redactor redactor)
    {
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;
        sb.AppendLine("# Dhan trial report (P0-13)").AppendLine();
        sb.AppendLine(inv, $"Generated {IstTime.ToIst(r.GeneratedAt):yyyy-MM-dd HH:mm} IST · mode: {(r.Offline ? "offline (cached responses)" : "live")} · read-only endpoints only").AppendLine();

        sb.AppendLine("## 1. Token flow").AppendLine();
        if (r.Profile is { } p)
        {
            sb.AppendLine(inv, $"- Profile: token valid until **{p.TokenValidity ?? "?"}**, Data API plan **{p.DataPlan ?? "?"}** (valid until {p.DataValidity ?? "?"}), active segments: {p.ActiveSegment ?? "?"}");
        }

        sb.AppendLine().AppendLine("| Time (IST) | Step | Result | Detail |").AppendLine("|---|---|---|---|");
        foreach (var e in r.TokenEvents.OrderBy(e => e.At))
        {
            sb.AppendLine(inv, $"| {IstTime.ToIst(e.At):MM-dd HH:mm} | {e.Step} | {(e.Ok ? "ok" : "**failed**")} | {e.Detail} |");
        }

        sb.AppendLine().AppendLine("## 2. Instrument master").AppendLine();
        sb.AppendLine(inv, $"- Rows: {r.MasterRows:N0} total, {r.MasterNseRows:N0} NSE equity/index kept. Columns: `{string.Join(", ", r.MasterHeader)}`").AppendLine();
        sb.AppendLine("| Requested | Kind | Status | security_id | Segment | Instrument | Symbol | Candidates |").AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var x in r.Resolutions)
        {
            sb.AppendLine(inv, $"| {x.Requested} | {x.Kind} | {x.Status} | {x.Row?.SecurityId} | {x.ExchangeSegment} | {x.Row?.Instrument} | {x.Row?.TradingSymbol} | {string.Join("; ", x.Candidates.Take(5).Select(c => $"{c.TradingSymbol}/{c.DisplayName}/{c.SecurityId}"))} |");
        }

        sb.AppendLine().AppendLine("## 3. Timestamp semantics").AppendLine();
        sb.AppendLine(r.Semantics is null ? "- not determined (no full sessions fetched)" : Fmt($"- Intraday: epoch = **{r.Semantics.Epoch}**, bar label = **{r.Semantics.Label}** — {r.Semantics.Evidence}"));
        if (r.DailyEpochNote is not null)
        {
            sb.AppendLine($"- Daily: {r.DailyEpochNote}");
        }

        sb.AppendLine().AppendLine("## 4. 1-minute history quality").AppendLine();
        sb.AppendLine("Expected bars per day = best-covered series that day, capped at 375 (09:15–15:29 IST).").AppendLine();
        sb.AppendLine("| Symbol | Window | Bars | Days | Complete % | Missing days | Missing min | Zero-vol | Dupes | Out-of-order | Out-of-session | Bad price | First bar | Last bar | Errors |").AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var h in r.History)
        {
            var q = h.Quality;
            sb.AppendLine(inv, $"| {h.Symbol} | {h.Window} | {h.IntradayBars} | {q?.TradingDays} | {q?.Completeness:F2} | {q?.MissingDays.Count} | {q?.MissingMinutes} | {q?.ZeroVolume} | {q?.Duplicates} | {q?.OutOfOrder} | {q?.OutOfSession} | {q?.BadPrice} | {h.FirstBar:yyyy-MM-dd HH:mm} | {h.LastBar:yyyy-MM-dd HH:mm} | {string.Join("; ", h.Errors)} |");
        }

        sb.AppendLine().AppendLine("### Worst days (most missing minutes)").AppendLine();
        foreach (var h in r.History.Where(h => h.Quality is { ShortDays.Count: > 0 }).OrderByDescending(h => h.Quality!.MissingMinutes).Take(5))
        {
            var worst = string.Join(", ", h.Quality!.ShortDays.OrderByDescending(d => d.MissingMinutes).Take(5).Select(d => Fmt($"{d.Date:MM-dd} ({d.Bars}/{d.Expected})")));
            sb.AppendLine(inv, $"- **{h.Symbol}**: {worst}");
        }

        sb.AppendLine().AppendLine("## 5. Daily candle vs derived from 1-minute").AppendLine();
        sb.AppendLine("| Symbol | Days | Open ≠ | High ≠ | Low ≠ | Close ≠ | Volume ≠ | Worst vol ratio | Price jumps (possible unadjusted split/bonus) |").AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var h in r.History.Where(h => h.Daily is not null))
        {
            var d = h.Daily!;
            sb.AppendLine(inv, $"| {h.Symbol} | {d.DaysCompared} | {d.OpenMismatch} | {d.HighMismatch} | {d.LowMismatch} | {d.CloseMismatch} | {d.VolumeMismatch} | {d.WorstVolumeRatio:F3} | {string.Join(", ", h.Discontinuities.Select(x => x.ToString("yyyy-MM-dd", inv)))} |");
        }

        sb.AppendLine().AppendLine("## 6. History depth (RELIANCE, 30-day windows ending N years ago)").AppendLine();
        sb.AppendLine("| Years back | Bars | Error |").AppendLine("|---|---|---|");
        foreach (var d in r.Depth.OrderBy(d => d.YearsBack))
        {
            sb.AppendLine(inv, $"| {d.YearsBack} | {d.Bars} | {d.Error} |");
        }

        sb.AppendLine().AppendLine("## 7. Limits and timing").AppendLine();
        sb.AppendLine("| Endpoint | Calls | Median ms | p95 ms | Max ms | Errors |").AppendLine("|---|---|---|---|---|---|");
        foreach (var e in r.Endpoints)
        {
            sb.AppendLine(inv, $"| {e.Endpoint} | {e.Calls} | {e.MedianMs:F0} | {e.P95Ms:F0} | {e.MaxMs:F0} | {string.Join(", ", e.Errors.Select(kv => $"{kv.Key}×{kv.Value}"))} |");
        }

        if (r.IntervalForm is not null)
        {
            sb.AppendLine().AppendLine(inv, $"- Intraday `interval` accepted as: **{r.IntervalForm}**. Largest single response seen: **{r.MaxBarsPerRequest:N0} bars** (90-day chunks).");
        }

        if (r.Burst is null)
        {
            sb.AppendLine().AppendLine("- Burst test: not run (opt-in with `--burst`; documented limit 5 requests/second, 100,000/day). Estimates below assume the documented limit.");
        }

        if (r.Burst is { } b)
        {
            sb.AppendLine().AppendLine(inv, $"- Burst test: {b.Requests} concurrent daily requests → {b.Succeeded} ok, {b.Throttled} throttled (retried), errors: {(b.Errors.Count == 0 ? "none" : string.Join(", ", b.Errors.Select(kv => $"{kv.Key}×{kv.Value}")))}. Documented limit: 5 requests/second, 100,000/day.");
        }

        if (r.Estimate is { } est)
        {
            sb.AppendLine().AppendLine("### EOD fetch extrapolation").AppendLine();
            sb.AppendLine(inv, $"- {est.Instruments} instruments × (1-day 1-minute + daily candle): sequential **{est.SequentialSeconds / 60:F1} min**, at the 5 req/s limit with parallelism **{est.RateLimitedSeconds / 60:F1} min** (NFR-2: whole nightly cycle < 45 min).");
            sb.AppendLine(inv, $"- First backfill of 5 years per instrument ≈ {est.BackfillCallsPerInstrument} calls → **{est.BackfillSeconds / 60:F1} min** for {est.Instruments} instruments at 5 req/s.");
        }

        if (r.Probes.Count > 0)
        {
            sb.AppendLine().AppendLine("## 8. Error shapes and range probes").AppendLine();
            sb.AppendLine("| Probe | HTTP | Code | Message | Bars |").AppendLine("|---|---|---|---|---|");
            foreach (var probe in r.Probes)
            {
                sb.AppendLine(inv, $"| {probe.Name} | {probe.Status} | {probe.Code} | {probe.Message} | {probe.Bars} |");
            }
        }

        if (r.Notes.Count > 0)
        {
            sb.AppendLine().AppendLine("## Notes").AppendLine();
            foreach (var n in r.Notes)
            {
                sb.AppendLine($"- {n}");
            }
        }

        return redactor.Redact(sb.ToString());
    }

    private static string Fmt(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
