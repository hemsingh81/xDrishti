using System.Globalization;
using System.Text.RegularExpressions;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>Lists the jobs a training report can read (AU-14), a bounded page at a time: the one place a screen finds the job id to pass as <c>simJob</c>. Read only.</summary>
internal sealed partial class ListSimReportJobsHandler(ITrainingJobProgress store) : IQueryHandler<ListSimReportJobsQuery, Result<SimReportJobsView>>
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;

    public async ValueTask<Result<SimReportJobsView>> HandleAsync(ListSimReportJobsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var limit = DefaultLimit;
        if (!string.IsNullOrEmpty(query.Limit) && (!int.TryParse(query.Limit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxLimit))
        {
            return Result.Failure<SimReportJobsView>(ReportErrors.Invalid($"limit is a whole number from 1 to {MaxLimit}"));
        }

        var offset = 0;
        if (query.Cursor is not null && (!Cursor().IsMatch(query.Cursor) || !int.TryParse(query.Cursor.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out offset)))
        {
            return Result.Failure<SimReportJobsView>(ReportErrors.Invalid("the cursor is not one this endpoint returned"));
        }

        var rows = await store.ListProgressAsync(offset, limit + 1, cancellationToken);
        var page = rows.Take(limit).Select(s => new SimReportJobView(
            s.Job.Id, s.Job.State, s.Job.State == "completed" && s.Counts.Failed == 0 && s.Counts.Remaining == 0, s.Counts.Done, s.Counts.Total, s.Counts.Failed,
            s.Job.WindowFrom, s.Job.WindowTo, s.Job.HoldOutStart, s.Job.CreatedAt, s.Job.FinishedAt)).ToList();
        var next = rows.Count > limit ? "o" + (offset + limit).ToString(CultureInfo.InvariantCulture) : null;
        return Result.Success(new SimReportJobsView(TrainingCompletenessView.TrainingLabel, page, next));
    }

    [GeneratedRegex("^o[0-9]{1,6}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex Cursor();
}
