using System.Globalization;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Quality;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>
/// What the owner is told about the data pipeline (P1-12a, P1-14a): sessions are left with missing bars that a refetch may still add, instruments fail the data quality checks, and splits or bonus issues the
/// detection suggested wait for the owner (P1-10a). A failed scheduled run is no longer a data notice: since P1-13 it is the <c>service_failed</c> notice of the end-of-day service (governed by its notify-on-failure
/// setting), so the bell has one notice for it. Pure: the same state always gives the same notices, like the account notices of the bell.
/// </summary>
public static class DataNoticeRules
{
    public const string GapsOpen = "data_gaps_open";

    public const string QualityFailed = "data_quality_failed";

    public const string CorporateActionsPending = "data_corporate_actions_pending";

    private const int MaxReasonLength = 160;

    /// <param name="gaps">The gap counts.</param>
    /// <param name="lastQualityRun">The latest quality run over every instrument; without one nothing is said about quality.</param>
    /// <param name="gate">The quality gate now (<see cref="QualityGateRules"/>).</param>
    /// <param name="pendingActions">The corporate action suggestions waiting for a decision.</param>
    public static IReadOnlyList<DataNotice> Build(GapSummary gaps, QualityRunView? lastQualityRun = null, QualityGateView? gate = null, PendingActions? pendingActions = null)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        var notices = new List<DataNotice>();
        if (gaps.Open > 0)
        {
            var at = gaps.LastDetectedAt;
            var stamp = at is { } t ? t.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) : "0";
            var text = gaps.Open == 1 ? "1 trading session has missing price bars" : $"{gaps.Open} trading sessions have missing price bars";
            notices.Add(new DataNotice($"data:gaps_open:{stamp}:{gaps.Open}", GapsOpen, "warn", text, at));
        }

        if (lastQualityRun is not null && gate is { Ok: false } && gate.Reasons.Count > 0)
        {
            var text = gate.Blocked ? $"Data quality blocks the nightly plan: {Short(gate.Reasons[0])}" : Short(gate.Reasons[0]);
            notices.Add(new DataNotice(
                $"data:quality_failed:{lastQualityRun.Id}:{gate.FailedInstruments}:{(gate.Blocked ? "blocked" : "ok")}",
                QualityFailed,
                gate.Blocked ? "error" : "warn",
                text,
                lastQualityRun.FinishedAt ?? lastQualityRun.StartedAt));
        }

        if (pendingActions is { Count: > 0 } pending)
        {
            var text = pending.Count == 1 ? "1 suggested corporate action (split or bonus) to review" : $"{pending.Count} suggested corporate actions (splits or bonus issues) to review";
            var stamp = pending.LatestAt is { } found ? found.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) : "0";
            notices.Add(new DataNotice($"data:corporate_actions_pending:{stamp}:{pending.Count}", CorporateActionsPending, "warn", text, pending.LatestAt));
        }

        return notices;
    }

    private static string Short(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxReasonLength ? line : line[..MaxReasonLength] + "…";
    }
}
