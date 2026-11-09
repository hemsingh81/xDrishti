using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Market.Bars.Repair;

/// <summary>
/// The owner's repair of old holes in the stored 1-minute bars of one instrument (ISS-29, ADR 0233): for the stated closed sessions <paramref name="From"/> to <paramref name="To"/> (at most
/// <c>Data:Repair:MaxSessions</c>), asks the broker's existing Data API read for the days that have missing minutes and stores <b>only the minutes that are missing</b>; the merge is insert-only, so a stored
/// bar can never be changed. The whole read, fetch and merge holds the one writer slot of the data jobs (409 <c>data.job_running</c> while another job runs, also for a double submit). Never automatic, off while
/// <c>Data:Repair:MaxSessions</c> is 0 (409 <c>data.repair_disabled</c>), no new external endpoint, audited. 400 <c>data.invalid</c> for a window that is reversed, open, in the future, empty or longer than the setting;
/// <c>data.instrument_not_found</c>; 409 <c>data.dhan_not_available</c> / <c>data.dhan_not_connected</c>; <c>data.repair_fetch_failed</c>; <c>data.repair_store_failed</c>.
/// Repaired bars change the bars hash of the window: stored runs stay as they are but can no longer be reproduced from the bars, and their trade cards fail the BarsSha256 check. The gap records
/// (<c>market.bar_gaps</c>) are not updated by the repair.
/// </summary>
public sealed record RepairBarHolesCommand(Guid InstrumentId, DateOnly From, DateOnly To, string RequestedBy) : ICommand;

/// <summary>The body of the repair: the first and the last closed session of the window.</summary>
public sealed record RepairBarHolesRequest(DateOnly From, DateOnly To);

/// <summary>
/// What the repair did. <see cref="MissingAfter"/> is what the broker could not give. <see cref="Rejected"/> counts bars the broker returned that failed the bar checks.
/// <see cref="AggregateJobId"/> is the follow-up job, or null with <see cref="AggregateNote"/> saying why not.
/// </summary>
public sealed record RepairBarHolesResult(
    string Symbol, DateOnly From, DateOnly To, int SessionsChecked, int SessionsWithHoles, int MissingBefore, int Fetched, long Inserted, int MissingAfter, Guid? AggregateJobId, int Rejected = 0, string? AggregateNote = null);

internal sealed class RepairBarHolesHandler(
    IBarQueries queries,
    IIntradayBarSource source,
    IDataAccountStatus account,
    IFetchTargets targets,
    IBarStore bars,
    IDataSettings settings,
    ITradingCalendar tradingCalendar,
    IDataJobStore jobs,
    ICommandHandler<StartDataJobCommand, DataJobView> start,
    IAccountEventLog events,
    IMarketUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<RepairBarHolesHandler> logger) : ICommandHandler<RepairBarHolesCommand, RepairBarHolesResult>
{
    /// <summary>The longest calendar span a window may cover before the sessions are even counted: a loop over a typed range must stay bounded.</summary>
    private const int MaxSpanDays = 400;

    public async ValueTask<Result<RepairBarHolesResult>> HandleAsync(RepairBarHolesCommand command, CancellationToken cancellationToken)
    {
        var max = settings.RepairMaxSessions;
        if (max < 1)
        {
            return Result.Failure<RepairBarHolesResult>(Error.Conflict(DataErrors.RepairDisabledCode, "The repair of old bar holes is switched off (Data:Repair:MaxSessions is 0)."));
        }

        if (await queries.FindInstrumentAsync(command.InstrumentId, cancellationToken) is null)
        {
            return Result.Failure<RepairBarHolesResult>(DataErrors.InstrumentNotFound);
        }

        var now = clock.GetUtcNow();
        var calendar = await tradingCalendar.GetAsync(cancellationToken);
        var sessions = Sessions(command, calendar, now, max, out var invalid);
        if (invalid is { } problem)
        {
            return Result.Failure<RepairBarHolesResult>(DataErrors.Invalid(problem.Field, problem.Message));
        }

        var availability = await source.CheckAsync(cancellationToken);
        if (!availability.Available)
        {
            return Result.Failure<RepairBarHolesResult>(Error.Conflict(DataErrors.DhanNotAvailableCode, availability.Reason ?? "The Dhan Data API fetch is not available."));
        }

        var state = await account.GetAsync(cancellationToken);
        if (!state.CanFetch)
        {
            return Result.Failure<RepairBarHolesResult>(Error.Conflict(DataErrors.DhanNotConnectedCode, state.Reason ?? "There is no connected primary Data account."));
        }

        var target = (await targets.ListAsync(cancellationToken)).FirstOrDefault(t => t.InstrumentId == command.InstrumentId);
        if (target is null)
        {
            return Result.Failure<RepairBarHolesResult>(Error.Conflict(DataErrors.DhanNotAvailableCode, "Dhan cannot be asked for this instrument: it is not tracked, is retired or has no Dhan id."));
        }

        // The one writer slot: no other data job (CSV load, Dhan fetch, undo) can write bars while this one reads, fetches and merges, and a second submit is refused.
        var job = DataJob.Start([DataSteps.Dhan], false, command.RequestedBy, now, DataJobTrigger.Manual, false, command.InstrumentId);
        if (!await jobs.TryStartAsync(job, DataLimits.StaleAfter, now, cancellationToken))
        {
            return Result.Failure<RepairBarHolesResult>(DataErrors.JobRunning);
        }

        Result<RepairBarHolesResult> outcome;
        try
        {
            outcome = await RepairAsync(command, target, calendar, sessions, now, cancellationToken);
        }
        catch
        {
            job.Fail(clock.GetUtcNow(), "The repair of old bar holes stopped unexpectedly.");
            await jobs.PublishAsync(job, CancellationToken.None);
            throw;
        }

        job.AddWarning(outcome.IsFailure ? $"Repair of old bar holes: {outcome.Error.Message}" : $"Repair of old bar holes for {target.Symbol}, {command.From:yyyy-MM-dd} to {command.To:yyyy-MM-dd}: {outcome.Value.Inserted} minutes inserted.");
        if (outcome.IsFailure)
        {
            job.Fail(clock.GetUtcNow(), outcome.Error.Message);
        }
        else
        {
            job.Finish(clock.GetUtcNow());
        }

        await jobs.PublishAsync(job, CancellationToken.None);
        if (outcome.IsFailure || outcome.Value.Inserted == 0)
        {
            return outcome;
        }

        // The slot is free again: the aggregates are rebuilt by a job of their own.
        var started = await start.HandleAsync(
            new StartDataJobCommand(new StartDataJobRequest([DataSteps.Aggregate, DataSteps.Quality]), command.RequestedBy, DataJobTrigger.Manual, command.InstrumentId), cancellationToken);
        var note = started.IsSuccess
            ? null
            : started.Error.Code == DataErrors.JobRunning.Code
                ? "Another data job started meanwhile; run Refresh aggregates for this instrument afterwards."
                : $"The aggregates were not rebuilt: {started.Error.Message}";
        return Result.Success(outcome.Value with { AggregateJobId = started.IsSuccess ? started.Value.Id : null, AggregateNote = note });
    }

    private async ValueTask<Result<RepairBarHolesResult>> RepairAsync(
        RepairBarHolesCommand command, FetchTarget target, TradingCalendar calendar, List<DateOnly> sessions, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var stored = (await bars.ReadAsync(command.InstrumentId, Midnight(sessions[0]).ToUniversalTime(), Midnight(sessions[^1].AddDays(1)).ToUniversalTime(), cancellationToken)).Select(b => b.EpochSeconds).ToHashSet();
        var missing = new HashSet<long>();
        var holeDays = new List<DateOnly>();
        foreach (var day in sessions)
        {
            var before = missing.Count;
            var session = calendar.SessionOf(day)!.Value;
            var open = Midnight(day).AddMinutes(session.FromMinute).ToUnixTimeSeconds();
            for (var minute = 0; minute < session.Minutes; minute++)
            {
                if (!stored.Contains(open + (minute * 60L)))
                {
                    missing.Add(open + (minute * 60L));
                }
            }

            if (missing.Count > before)
            {
                holeDays.Add(day);
            }
        }

        if (missing.Count == 0)
        {
            await AuditAsync(command, target, 0, 0, now, cancellationToken);
            return Result.Success(new RepairBarHolesResult(target.Symbol, command.From, command.To, sessions.Count, 0, 0, 0, 0, 0, null));
        }

        var plan = new IntradayFetchPlan([new IntradayTarget(target.InstrumentId, target.Symbol, target.Type, target.SecurityId, target.Segment, DhanFetchPlanner.GapWindows(holeDays, calendar, now))]);
        await foreach (var result in source.FetchAsync(plan, cancellationToken))
        {
            if (result.Failure is { Kind: IntradayFailureKind.Unauthorized })
            {
                return Result.Failure<RepairBarHolesResult>(Error.Conflict(DataErrors.DhanNotConnectedCode, "Dhan refused the access token of the Data account. Reconnect it under Accounts, then run again."));
            }

            if (result.Failure is { } failure)
            {
                return Result.Failure<RepairBarHolesResult>(Error.Conflict(DataErrors.RepairFetchFailedCode, failure.Message));
            }

            // Only the minutes that are missing, and only bars that pass the bar checks; the merge itself is insert-only.
            var checkedBars = IntradayBarCheck.Check(result.Bars);
            var fill = checkedBars.Bars.Where(b => missing.Contains(b.EpochSeconds)).DistinctBy(b => b.EpochSeconds).ToArray();
            BarMergeResult merge;
            try
            {
                await using var session = await bars.BeginInsertOnlyMergeAsync(target.InstrumentId, BarSource.Dhan, cancellationToken);
                await session.AddAsync(fill, cancellationToken);
                merge = await session.CompleteAsync(null, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // the database refusing the bars is an expected failure of this use case: it becomes a Result and nothing is stored (the merge is one transaction)
            catch (Exception ex)
#pragma warning restore CA1031
            {
                DataLog.BarHolesRepairFailed(logger, target.Symbol, ex);
                return Result.Failure<RepairBarHolesResult>(Error.Conflict(DataErrors.RepairStoreFailedCode, "The bars could not be stored; nothing was changed."));
            }

            DataLog.BarHolesRepaired(logger, target.Symbol, command.From, command.To, missing.Count, merge.Inserted, (int)checkedBars.Rejected);
            await AuditAsync(command, target, missing.Count, merge.Inserted, now, cancellationToken);
            return Result.Success(new RepairBarHolesResult(
                target.Symbol, command.From, command.To, sessions.Count, holeDays.Count, missing.Count, fill.Length, merge.Inserted, missing.Count - (int)merge.Inserted, null, (int)checkedBars.Rejected));
        }

        return Result.Failure<RepairBarHolesResult>(Error.Conflict(DataErrors.RepairFetchFailedCode, "Dhan returned nothing for this instrument."));
    }

    private async ValueTask AuditAsync(RepairBarHolesCommand command, FetchTarget target, int missing, long inserted, DateTimeOffset now, CancellationToken cancellationToken)
    {
        events.Record(AccountEvent.Record(
            AccountEventType.BarHolesRepaired, null, $"instrument={target.Symbol} window={command.From:yyyy-MM-dd}..{command.To:yyyy-MM-dd} missing={missing} inserted={inserted}", command.RequestedBy, now));
        await unitOfWork.SaveAsync(cancellationToken);
    }

    private static List<DateOnly> Sessions(RepairBarHolesCommand command, TradingCalendar calendar, DateTimeOffset now, int max, out (string Field, string Message)? invalid)
    {
        invalid = null;
        var days = new List<DateOnly>();
        if (command.To < command.From)
        {
            invalid = ("from", "The window is reversed: from is after to.");
        }
        else if (command.To.DayNumber - command.From.DayNumber > MaxSpanDays)
        {
            invalid = ("to", $"The window spans more than {MaxSpanDays} days.");
        }
        else if (command.To > calendar.LastClosedSession(now))
        {
            invalid = ("to", "The window must end on a session that has closed.");
        }
        else
        {
            for (var day = command.From; day <= command.To; day = day.AddDays(1))
            {
                if (calendar.IsTradingDay(day))
                {
                    days.Add(day);
                }
            }

            if (days.Count == 0)
            {
                invalid = ("from", "The window has no trading session.");
            }
            else if (days.Count > max)
            {
                invalid = ("to", $"The window has {days.Count} sessions; at most {max} may be repaired in one go (Data:Repair:MaxSessions).");
            }
        }

        return days;
    }

    private static DateTimeOffset Midnight(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
}
