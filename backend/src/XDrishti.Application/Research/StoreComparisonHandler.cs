using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Research;

/// <summary>
/// Keeps a comparison (spec P4-05, D10): validates it without the database, reads the instant once, asks the store to write the header and logs the outcome (ids 4425 to 4427). A conflict under a stored key is logged at error and returned as
/// <c>store.run_conflict</c>, as for a run; an unexpected exception is logged by code and raised again. Nothing of the table or the trades is logged.
/// </summary>
internal sealed class StoreComparisonHandler(IRunStore store, TimeProvider time, ILogger<StoreComparisonHandler> logger) : ICommandHandler<StoreComparisonCommand, StoreOutcome>
{
    /// <summary>The most members a comparison has (the most candidates of <c>ExitFlags</c>).</summary>
    public const int MaxMembers = 16;

    public async ValueTask<Result<StoreOutcome>> HandleAsync(StoreComparisonCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var problems = Check(command);
        if (problems.Count > 0)
        {
            return Result.Failure<StoreOutcome>(StoreErrors.RunInvalid(problems));
        }

        var comparison = command.Comparison!;
        var report = comparison.Report;
        var members = comparison.Candidates.Select(c => new ComparisonMemberToStore(c.Run.RunKey, c.Id, c.GatedBlocked)).ToList();
        var toStore = new ComparisonToStore(comparison.ComparisonKey, comparison.EntriesKey, comparison.Candidates[0].Run.Strategy.Id, report.Unit, report.CommonEntries, members, command.Record, command.Actor, time.GetUtcNow());
        var shortKey = comparison.ComparisonKey[..12];
        var started = time.GetTimestamp();
        Result<StoreOutcome> stored;
        try
        {
            stored = await store.StoreComparisonAsync(toStore, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            ComparisonStoreLog.Failed(logger, shortKey, StoreErrors.WriteFailedCode);
            throw;
        }

        if (stored.IsSuccess)
        {
            var outcome = stored.Value.WasAlreadyStored ? "already stored" : "stored";
            var totalMs = (long)time.GetElapsedTime(started).TotalMilliseconds;
            ComparisonStoreLog.Completed(logger, shortKey, members.Count, outcome, totalMs);
        }
        else if (stored.Error.Code == StoreErrors.RunConflictCode)
        {
            ComparisonStoreLog.Conflict(logger, shortKey, string.Join(", ", (stored.Error.Fields ?? []).Select(f => f.Field)));
        }
        else
        {
            ComparisonStoreLog.Failed(logger, shortKey, stored.Error.Code);
        }

        return stored;
    }

    private static List<FieldError> Check(StoreComparisonCommand command)
    {
        var problems = new List<FieldError>();
        if (command.Comparison is not { } comparison)
        {
            problems.Add(new FieldError("comparison", "The comparison is missing."));
            return problems;
        }

        if (command.Record is null)
        {
            problems.Add(new FieldError("record", "The record text is missing."));
        }

        if (string.IsNullOrWhiteSpace(command.Actor) || command.Actor.Length > 100)
        {
            problems.Add(new FieldError("actor", "The actor is required and at most 100 characters."));
        }

        if (!RunRecordText.IsHex64(comparison.ComparisonKey) || !RunRecordText.IsHex64(comparison.EntriesKey))
        {
            problems.Add(new FieldError("comparison.keys", "The comparison key and the entries key are 64 lower-case hex characters."));
        }

        if (comparison.Candidates.Count is < 2 or > MaxMembers)
        {
            problems.Add(new FieldError("comparison.candidates", $"A comparison has 2 to {MaxMembers} candidates."));
            return problems;
        }

        if (comparison.Candidates.Any(c => !RunRecordText.IsHex64(c.Run.RunKey) || string.IsNullOrEmpty(c.Id) || c.Id.Length > 200))
        {
            problems.Add(new FieldError("comparison.candidates", "A candidate has a run key that is not 64 lower-case hex characters, or an id that is empty or over 200 characters."));
        }

        if (comparison.Candidates.Select(c => c.Run.RunKey).Distinct(StringComparer.Ordinal).Count() != comparison.Candidates.Count)
        {
            problems.Add(new FieldError("comparison.candidates", "Two candidates have the same run key."));
        }

        if (comparison.Candidates.Any(c => c.GatedBlocked < 0))
        {
            problems.Add(new FieldError("comparison.candidates.gatedBlocked", "A gated count is not negative."));
        }

        return problems;
    }
}
