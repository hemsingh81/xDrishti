using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay;

/// <summary>
/// Steps the sessions D to E of one replay in order, in memory (SR-01 part A): each session needs the book the one before left. The job (part B) steps it one session at a time through <see cref="StepAsync"/> and keeps the cursor, the yield rules and cancel. A failed session stops the run with its reason; nothing is skipped.
/// </summary>
internal sealed class ReplayDriver(IReplayFacts facts, ReplaySessionRunner runner)
{
    /// <summary>The error code of a range that reaches the hold-out and its embargo.</summary>
    public const string HoldOutCode = "replay.holdout";

    /// <summary>The error code of parameters that cannot be used.</summary>
    public const string InvalidCode = "replay.invalid";

    /// <summary>
    /// The checks of a run and its context: the parameters, the hold-out clamp, frozen cell statistics as of the start, the charge sheet. <paramref name="stored"/> and <paramref name="versions"/> are the settings snapshot and the strategy version map stored with a run that resumes (null for a run that starts: the facts take today's then).
    /// Reads only; the book is empty.
    /// </summary>
    public async ValueTask<Result<ReplayPrepared>> PrepareAsync(ReplayParams parameters, ReplaySettingsSnapshot? stored, IReadOnlyDictionary<string, IReadOnlyList<Abstractions.Research.StoredVersion>>? versions, CancellationToken cancellationToken, string? learningSources = null, bool learningFixed = false)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var problems = parameters.Problems();
        if (problems.Count > 0)
        {
            return Result.Failure<ReplayPrepared>(Error.Validation(InvalidCode, string.Join("; ", problems)));
        }

        DateOnly last;
        try
        {
            last = await facts.LastReadableSessionAsync(cancellationToken);
        }
        catch (ReplayHoldOutException)
        {
            return Result.Failure<ReplayPrepared>(Error.Conflict(HoldOutCode, "No hold-out block is fixed, so the replay cannot know how far it may read."));
        }

        if (parameters.To > last)
        {
            return Result.Failure<ReplayPrepared>(Error.Conflict(HoldOutCode, $"The range reaches the hold-out; the last allowed date is {last:yyyy-MM-dd}."));
        }

        facts.BeginRun(parameters.To, stored, versions, learningSources, learningFixed);
        if ((await facts.CellsAsync(parameters, parameters.From, cancellationToken)).Cells.Count == 0)
        {
            return Result.Failure<ReplayPrepared>(Error.Validation(InvalidCode, "no cell statistics as of " + parameters.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ": a replay without them would only draw a flat curve"));
        }

        var sheet = await facts.CostSheetAsync(parameters, cancellationToken);
        if (sheet.IsFailure)
        {
            return Result.Failure<ReplayPrepared>(sheet.Error);
        }

        var calendar = await facts.CalendarAsync(cancellationToken);
        var settings = await facts.SettingsAsync(cancellationToken);
        var costs = parameters.CostSheetId is { } id ? Research.CostChoice.Sheet(id, parameters.Capital) : Research.CostChoice.Gross;
        var sessions = new List<DateOnly>();
        for (var day = parameters.From; day <= parameters.To; day = day.AddDays(1))
        {
            if (calendar.IsTradingDay(day))
            {
                sessions.Add(day);
            }
        }

        var released = await facts.ReleasedHoldOutAsync(cancellationToken);
        return Result.Success(new ReplayPrepared(new ReplayRunContext(parameters, settings, costs, sheet.Value, calendar, parameters.To, released), new ReplayBook(parameters.Capital), sessions));
    }

    /// <summary>Replays one session of a prepared run on its book.</summary>
    public ValueTask<Result<ReplaySessionRecord>> StepAsync(ReplayPrepared prepared, DateOnly session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        return runner.RunAsync(prepared.Run, session, prepared.Book, cancellationToken);
    }

    public async ValueTask<Result<ReplayRunResult>> RunAsync(ReplayParams parameters, CancellationToken cancellationToken, Action<ReplaySessionRecord>? onSession = null)
    {
        var prepared = await PrepareAsync(parameters, null, null, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<ReplayRunResult>(prepared.Error);
        }

        var run = prepared.Value;
        var records = new List<ReplaySessionRecord>();
        var labels = new SortedSet<string>(HindsightLabels.For(parameters).Where(l => l != HindsightLabels.FiltersWithoutHistory), StringComparer.Ordinal);
        foreach (var day in run.Sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await StepAsync(run, day, cancellationToken);
            if (record.IsFailure)
            {
                return Result.Failure<ReplayRunResult>(record.Error);
            }

            records.Add(record.Value);
            labels.UnionWith(record.Value.Labels);
            onSession?.Invoke(record.Value);
        }

        return Result.Success(new ReplayRunResult(records, run.Book, run.Book.FiguresAsOf(parameters.To), [.. labels]));
    }
}
