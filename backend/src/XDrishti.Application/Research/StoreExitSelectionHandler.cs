using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research;

/// <summary>
/// Keeps an exit choice (spec P4-06, D10): validates it without the database, derives the selection key, reads the instant once, asks the store to write the header and the fold rows and logs the outcome (ids 4428 to 4430). A conflict under a stored key
/// is logged at error and returned as <c>store.run_conflict</c>, as for a run; an unexpected exception is logged by code and raised again. Nothing of the record or the trades is logged.
/// </summary>
internal sealed class StoreExitSelectionHandler(IExitSelectionStore store, TimeProvider time, ILogger<StoreExitSelectionHandler> logger) : ICommandHandler<StoreExitSelectionCommand, StoreOutcome>
{
    public async ValueTask<Result<StoreOutcome>> HandleAsync(StoreExitSelectionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = Check(command);
        if (problems.Count > 0)
        {
            return Result.Failure<StoreOutcome>(StoreErrors.RunInvalid(problems));
        }

        var selection = command.Selection!;
        var key = ExitSelectionKey.Of(command.ComparisonKey, selection);
        var folds = selection.Folds.SelectMany(f => f.Rows.Select(r => new ExitSelectionFoldToStore(f.Index, f.TestStart, f.TestEnd, r.Id, r.Qualified, r.TrainTrades, r.TrainScore, r.TestTrades, r.TestScore, string.Equals(r.Id, f.Winner, StringComparison.Ordinal)))).ToList();
        var toStore = new ExitSelectionToStore(
            key, command.ComparisonKey, command.StrategyId, command.Timeframe, command.Side, command.Style, "*", selection.FinalCandidate, selection.Stability.Stable, command.RecordJson, command.RecordSha256, folds, command.Actor, time.GetUtcNow());
        var shortKey = key[..12];
        var started = time.GetTimestamp();
        Result<StoreOutcome> stored;
        try
        {
            stored = await store.StoreAsync(toStore, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ExitSelectionStoreLog.Failed(logger, shortKey, StoreErrors.WriteFailedCode);
            throw;
        }

        if (stored.IsSuccess)
        {
            var totalMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            var chosen = selection.FinalCandidate ?? "none";
            var stable = selection.Stability.Stable?.ToString() ?? "none";
            var outcome = stored.Value.WasAlreadyStored ? "already stored" : "stored";
            ExitSelectionStoreLog.Completed(logger, shortKey, selection.Input.Cell, folds.Count, chosen, stable, outcome, totalMs);
        }
        else if (stored.Error.Code == StoreErrors.RunConflictCode)
        {
            ExitSelectionStoreLog.Conflict(logger, shortKey, string.Join(", ", (stored.Error.Fields ?? []).Select(f => f.Field)));
        }
        else
        {
            ExitSelectionStoreLog.Failed(logger, shortKey, stored.Error.Code);
        }

        return stored;
    }

    private static List<FieldError> Check(StoreExitSelectionCommand command)
    {
        var problems = new List<FieldError>();
        if (command.Selection is null)
        {
            problems.Add(new FieldError("selection", "The exit choice is missing."));
            return problems;
        }

        if (!RunRecordText.IsHex64(command.ComparisonKey))
        {
            problems.Add(new FieldError("comparisonKey", "The comparison key is 64 lower-case hex characters."));
        }

        if (!RunRecordText.IsHex64(command.RecordSha256))
        {
            problems.Add(new FieldError("recordSha256", "The digest is 64 lower-case hex characters."));
        }

        if (string.IsNullOrWhiteSpace(command.RecordJson))
        {
            problems.Add(new FieldError("recordJson", "The record text is missing."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        if (string.IsNullOrWhiteSpace(command.StrategyId) || command.StrategyId.Length > 64 || string.IsNullOrWhiteSpace(command.Timeframe) || command.Timeframe.Length > 8 || string.IsNullOrWhiteSpace(command.Side) || command.Side.Length > 8 || string.IsNullOrWhiteSpace(command.Style) || command.Style.Length > 16)
        {
            problems.Add(new FieldError("cell", "The strategy, timeframe, side and style are required and short."));
        }

        if (command.Selection.Input.Options.Problems().Count > 0)
        {
            problems.Add(new FieldError("selection.options", "The options are not usable."));
        }

        return problems;
    }
}
