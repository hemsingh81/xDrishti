using System.Globalization;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Market.Bars.Eod;

/// <summary>
/// What the owner is told about the data pipeline (P1-12a, P1-14a): the last scheduled run failed, sessions are left with missing bars that a refetch may still add, and instruments fail
/// the data quality checks, and splits or bonus issues the detection suggested wait for the owner (P1-10a). Pure: the same state always gives the same notices, like the account notices of the bell.
/// </summary>
public static class DataNoticeRules
{
    public const string RunFailed = "data_run_failed";

    public const string GapsOpen = "data_gaps_open";

    public const string QualityFailed = "data_quality_failed";

    public const string CorporateActionsPending = "data_corporate_actions_pending";

    private const int MaxReasonLength = 160;

    /// <param name="lastScheduledRun">The latest automatic run (any state), if any.</param>
    /// <param name="gaps">The gap counts.</param>
    /// <param name="lastQualityRun">The latest quality run over every instrument; without one nothing is said about quality.</param>
    /// <param name="gate">The quality gate now (<see cref="QualityGateRules"/>).</param>
    /// <param name="pendingActions">The corporate action suggestions waiting for a decision.</param>
    public static IReadOnlyList<DataNotice> Build(DataJob? lastScheduledRun, GapSummary gaps, QualityRunView? lastQualityRun = null, QualityGateView? gate = null, PendingActions? pendingActions = null)
    {
        ArgumentNullException.ThrowIfNull(gaps);
        var notices = new List<DataNotice>();
        if (lastScheduledRun is { Status: DataJobStatus.Failed } failed)
        {
            var day = IndiaTime.Today(failed.StartedAt).ToString("d MMM", CultureInfo.InvariantCulture);
            var reason = string.IsNullOrWhiteSpace(failed.Error) ? "see the update on the Data page" : Short(failed.Error);
            notices.Add(new DataNotice($"data:run_failed:{failed.Id}", RunFailed, "error", $"The automatic data update of {day} failed: {reason}", failed.FinishedAt ?? failed.StartedAt));
        }

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
