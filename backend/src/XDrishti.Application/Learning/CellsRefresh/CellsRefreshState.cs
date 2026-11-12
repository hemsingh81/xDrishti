using System.Globalization;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Learning.CellsRefresh;

/// <summary>
/// The newest state of the <c>cells-refresh</c> service in a few words, for the gate reason and the bell (spec LC-01, D5, D8): <c>paused</c>, <c>failed on &lt;date&gt;: &lt;code&gt;</c>, <c>never ran</c> or <c>last ran &lt;date&gt;</c>. Reads the schedule and the run history only.
/// </summary>
internal sealed class CellsRefreshState(IServiceScheduleReader schedules, IServiceRunStore runs)
{
    public async ValueTask<string> DescribeAsync(CancellationToken cancellationToken)
    {
        var info = await schedules.GetAsync(CellsRefreshRules.ServiceId, cancellationToken);
        if (info is not { Enabled: true })
        {
            return "paused";
        }

        var latest = await runs.GetLatestAsync([CellsRefreshRules.ServiceId], cancellationToken);
        if (!latest.TryGetValue(CellsRefreshRules.ServiceId, out var run))
        {
            return "never ran";
        }

        var day = IndiaTime.Today(run.StartedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return run.Status == ServiceRunStatus.Failed ? $"failed on {day}: {CodeOf(run.Message)}" : $"last ran {day}";
    }

    /// <summary>The code a failed run message starts with (<c>cells_refresh.no_snapshot: ...</c>), else a short start of the text.</summary>
    internal static string CodeOf(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "no reason recorded";
        }

        var colon = message.IndexOf(':', StringComparison.Ordinal);
        var head = colon is > 0 and <= 60 ? message[..colon] : message;
        return head.Length <= 60 ? head : head[..60];
    }
}
