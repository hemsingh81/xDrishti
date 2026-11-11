using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay;

/// <summary>
/// Steps the sessions D to E of one replay in order, in memory (SR-01 part A): each session needs the book the one before left. Persistence, the cursor, the yield rules and cancel come with the job (part B). A failed session stops the run with its reason; nothing is skipped.
/// </summary>
internal sealed class ReplayDriver(IReplayFacts facts, ReplaySessionRunner runner)
{
    /// <summary>The error code of a range that reaches the hold-out and its embargo.</summary>
    public const string HoldOutCode = "replay.holdout";

    /// <summary>The error code of parameters that cannot be used.</summary>
    public const string InvalidCode = "replay.invalid";

    public async ValueTask<Result<ReplayRunResult>> RunAsync(ReplayParams parameters, CancellationToken cancellationToken, Action<ReplaySessionRecord>? onSession = null)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var problems = parameters.Problems();
        if (problems.Count > 0)
        {
            return Result.Failure<ReplayRunResult>(Error.Validation(InvalidCode, string.Join("; ", problems)));
        }

        DateOnly last;
        try
        {
            last = await facts.LastReadableSessionAsync(cancellationToken);
        }
        catch (ReplayHoldOutException)
        {
            return Result.Failure<ReplayRunResult>(Error.Conflict(HoldOutCode, "No hold-out block is fixed, so the replay cannot know how far it may read."));
        }

        if (parameters.To > last)
        {
            return Result.Failure<ReplayRunResult>(Error.Conflict(HoldOutCode, $"The range reaches the hold-out; the last allowed date is {last:yyyy-MM-dd}."));
        }

        if ((await facts.CellsAsync(parameters, parameters.From, cancellationToken)).Cells.Count == 0)
        {
            return Result.Failure<ReplayRunResult>(Error.Validation(InvalidCode, "no cell statistics as of " + parameters.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ": a replay without them would only draw a flat curve"));
        }

        var sheet = await facts.CostSheetAsync(parameters, cancellationToken);
        if (sheet.IsFailure)
        {
            return Result.Failure<ReplayRunResult>(sheet.Error);
        }

        var calendar = await facts.CalendarAsync(cancellationToken);
        var settings = await facts.SettingsAsync(cancellationToken);
        var costs = parameters.CostSheetId is { } id ? Research.CostChoice.Sheet(id, parameters.Capital) : Research.CostChoice.Gross;
        var run = new ReplayRunContext(parameters, settings, costs, sheet.Value, calendar, parameters.To);
        var book = new ReplayBook(parameters.Capital);
        var records = new List<ReplaySessionRecord>();
        var labels = new SortedSet<string>(HindsightLabels.For(parameters), StringComparer.Ordinal);
        for (var day = parameters.From; day <= parameters.To; day = day.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!calendar.IsTradingDay(day))
            {
                continue;
            }

            var record = await runner.RunAsync(run, day, book, cancellationToken);
            if (record.IsFailure)
            {
                return Result.Failure<ReplayRunResult>(record.Error);
            }

            records.Add(record.Value);
            labels.UnionWith(record.Value.Labels);
            onSession?.Invoke(record.Value);
        }

        return Result.Success(new ReplayRunResult(records, book, book.FiguresAsOf(parameters.To), [.. labels]));
    }
}
