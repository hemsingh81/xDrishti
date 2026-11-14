using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>
/// Extends a complete simulation to the newest complete stored session (SR-04, AC 10), under the same hold-out rule as the start (block 1's look, taken automatically when free). The cursor resumes at the next session; the stored days are not touched and each is rebuilt with the end
/// that was in force when it was settled, so their hashes stay. The added stretch is one more <c>system_replay</c> look. Nothing newer: 409 <c>sim.nothing_new</c> with the reason.
/// </summary>
internal sealed class ContinueSimDaysHandler(ISimDaysStore store, SimDaysHoldOut holdOut, ITradingCalendar calendars, ITestLedger ledger, SimDaysOptions options, TimeProvider time, ILogger<ContinueSimDaysHandler> logger) : ICommandHandler<ContinueSimDaysCommand, SimDaysView>
{
    public async ValueTask<Result<SimDaysView>> HandleAsync(ContinueSimDaysCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (await store.GetAsync(command.Id, cancellationToken) is not { } row)
        {
            return Result.Failure<SimDaysView>(SimDays.NotFound);
        }

        if (row.Phase != SimDays.Complete)
        {
            return Result.Failure<SimDaysView>(Error.Conflict(SimDays.NotCompleteCode, "Only a complete simulation can be continued; this one is " + row.Phase + "."));
        }

        if (await store.ActiveAsync(cancellationToken) is not null)
        {
            return Result.Failure<SimDaysView>(SimDays.Busy("Another simulation is active."));
        }

        if (await holdOut.HistoryAsync(cancellationToken) is not { } history || history.LastComplete <= row.End)
        {
            return Result.Failure<SimDaysView>(Error.Conflict(SimDays.NothingNewCode, "No complete session newer than the end is stored."));
        }

        var answer = await holdOut.ResolveAsync(history.LastComplete, cancellationToken);
        var calendar = await calendars.GetAsync(cancellationToken);
        if (answer.End <= row.End && answer.Claim is not null)
        {
            // clamped at the cut with block 1's look free: claim it now (Continue extends once it is stored)
            answer = await holdOut.ClaimAsync(answer, cancellationToken);
            return Result.Failure<SimDaysView>(Error.Conflict(SimDays.NothingNewCode, answer.Note!));
        }

        if (answer.End <= row.End || SimDaysSupport.Next(calendar, row.Cursor, answer.End) is not { } cursor)
        {
            return Result.Failure<SimDaysView>(Error.Conflict(SimDays.NothingNewCode, answer.Note ?? "No trading session newer than the end is stored."));
        }

        // the look first: the trial-ledger rows have ids fixed by the run and the new end, so a retry after a failure writes nothing twice; a look recorded for an extension that then fails to store over-counts (the safe side), it never loses one
        var now = time.GetUtcNow();
        var versions = ReplayJson.Read<Dictionary<string, List<StoredVersion>>>(row.VersionsJson) ?? [];
        await SimDaysLedger.RecordAsync(ledger, row.Id, row.InputHash, versions.ToDictionary(p => p.Key, p => (IReadOnlyList<StoredVersion>)p.Value, StringComparer.Ordinal), cursor, answer.End, now, cancellationToken);
        answer = await holdOut.ClaimAsync(answer, cancellationToken);
        if (!await store.ExtendAsync(row.Id, answer.End, history.LastComplete, cursor, answer.ReadThrough ?? row.ReadThrough, answer.Note ?? row.HoldOutNote, now, cancellationToken))
        {
            return Result.Failure<SimDaysView>(SimDays.Busy("The simulation moved meanwhile, or another one is active."));
        }
        SimDaysLog.Continued(logger, row.Id, answer.End);
        return Result.Success(SimDaysSupport.View(await store.GetAsync(row.Id, cancellationToken) ?? row, calendar, options));
    }
}
