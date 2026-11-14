using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Replay.Job;
using XDrishti.Domain.Platform.Services;

namespace XDrishti.Application.Replay.Sim;

/// <summary>
/// The one hosted runner of the day-by-day simulation (SR-04, ADR 0293), in the SR-01 job pattern: the table is the queue and the memory. It settles a day asked for (<c>settling</c>), and with auto-run on it repeats settle and advance through the <b>same commands as the manual buttons</b> until the end,
/// a pause (auto off, seen after the current day) or a cancel. The SR-01 yield rules (<see cref="ReplayYieldRules"/>: low disk, market hours, data jobs, an incomplete backfill, a simulate-everything chunk) are looked at before each day; a day is never cut short.
/// <para>
/// <b>Resume.</b> The book lives in memory. After a restart (or a Continue, or any doubt) it is rebuilt from the stored days, each stepped again with the end that was in force for it and checked against its stored hash (a mismatch ends the run as <c>error</c>, <c>sim.diverged</c>);
/// a day left <c>settling</c> is settled again from its start, with the same result. Settling is idempotent per (run, session): the store moves the phase only from <c>settling</c> on the same cursor.
/// </para>
/// It writes only the simulation tables through <see cref="ISimDaysStore"/>; it places and references no order.
/// </summary>
public sealed class SimDaysRunner(IServiceScopeFactory scopes, SimDaysOptions options, SystemReplayOptions replayOptions, TimeProvider time, ILogger<SimDaysRunner> logger)
{
    /// <summary>The recovery class (AU-01): convergent, a run goes on from its stored cursor.</summary>
    public const RecoveryClass Recovery = RecoveryClass.Convergent;

    private static readonly TimeSpan ContinueEvery = TimeSpan.FromMinutes(1);

    private readonly ReplayYieldRules _yield = new(replayOptions, time);
    private ActiveBook? _active;
    private DateTimeOffset? _continueCheckedAt;

    /// <summary>The reason the runner last waited, or null (for the status and the tests).</summary>
    public string? Waiting { get; private set; }

    /// <summary>The loop of the host: one tick every poll interval until the host stops.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TickAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
#pragma warning disable CA1031 // the loop of the host must survive a failed step; the run is rebuilt from what is stored at the next tick
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    Drop();
                    SimDaysLog.LoopFailed(logger, ex);
                }

                await Task.Delay(options.PollInterval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down: a day in flight stays settling and the next start settles it again
        }
        finally
        {
            Drop();
        }
    }

    /// <summary>One look at the active run: settle what is asked for and, with auto-run on, settle and advance day after day until it must wait, pauses or ends.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<ISimDaysStore>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await store.ActiveAsync(cancellationToken) is not { } row)
            {
                Drop();
                await AutoContinueAsync(services, store, cancellationToken);
                return;
            }

            if (_active is { } held && held.Id != row.Id)
            {
                Drop();
            }

            switch (row.Phase)
            {
                case SimDays.Idle when row.Auto:
                    if ((await services.GetRequiredService<ICommandHandler<SettleSimDayCommand, SimDaysView>>().HandleAsync(new SettleSimDayCommand(row.Id), cancellationToken)).IsFailure)
                    {
                        return;
                    }

                    break;
                case SimDays.Settling:
                    if (await _yield.ReasonAsync(services, true, cancellationToken) is { } reason)
                    {
                        Waiting = reason;
                        return;
                    }

                    Waiting = null;
                    if (!await SettleAsync(row, store, cancellationToken))
                    {
                        return;
                    }

                    break;
                case SimDays.Settled when row.Auto:
                    if ((await services.GetRequiredService<ICommandHandler<AdvanceSimDayCommand, SimDaysView>>().HandleAsync(new AdvanceSimDayCommand(row.Id), cancellationToken)).IsFailure)
                    {
                        return;
                    }

                    break;
                default:
                    return;
            }
        }
    }

    /// <summary>Settles the cursor day on the book in memory (rebuilt when it is not exactly the stored days); false when the run ended or moved.</summary>
    private async Task<bool> SettleAsync(SimDaysRunRow row, ISimDaysStore store, CancellationToken cancellationToken)
    {
        if (await BookAsync(row, store, cancellationToken) is not { } book)
        {
            return false;
        }

        SimDayOutcome day;
        try
        {
            var stepped = await book.StepAsync(row.Cursor, row.End, cancellationToken);
            if (stepped.IsFailure)
            {
                return await FailAsync(store, row.Id, stepped.Error.Code, stepped.Error.Message, cancellationToken);
            }

            day = stepped.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Drop(); // the book may have stepped part of the day: rebuild it; the day stays settling
            throw;
        }
#pragma warning disable CA1031 // a failed day (a read past the clamp, a defect) ends this run with its reason; nothing is skipped
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return await FailAsync(store, row.Id, ex is ReplayHoldOutException ? ReplayDriver.HoldOutCode : "sim.day_crashed", ex.GetType().Name + ": " + ex.Message, cancellationToken);
        }

        if (!await store.SettleAsync(row.Id, new SimDayPending(row.Cursor, day.RecordJson, day.TradesJson, day.EquityClose, day.ResultHash, row.End), time.GetUtcNow(), cancellationToken))
        {
            Drop(); // cancelled or moved meanwhile: what is in memory is no longer the stored run
            return false;
        }

        SimDaysLog.Settled(logger, row.Id, row.Cursor);
        return true;
    }

    /// <summary>The book of the run with every stored day stepped: the one in memory when it matches, else rebuilt and checked against the stored hashes.</summary>
    private async Task<SimDayBook?> BookAsync(SimDaysRunRow row, ISimDaysStore store, CancellationToken cancellationToken)
    {
        var days = await store.SnapshotsAsync(row.Id, cancellationToken);
        DateOnly? through = days.Count == 0 ? null : days[^1].Session;
        if (_active is { } held && held.Id == row.Id && held.Book.End == row.End && held.Book.Through == through)
        {
            return held.Book;
        }

        Drop();
        var scope = scopes.CreateAsyncScope();
        var kept = false;
        try
        {
            var services = scope.ServiceProvider;
            var opened = await SimDayBook.OpenAsync(services.GetRequiredService<ReplayDriver>(), services.GetRequiredService<IReplayFacts>(), row, cancellationToken);
            if (opened.IsFailure)
            {
                await FailAsync(store, row.Id, opened.Error.Code, opened.Error.Message, cancellationToken);
                return null;
            }

            var book = opened.Value;
            foreach (var stored in days)
            {
                var again = await book.StepAsync(stored.Session, stored.EndInForce, cancellationToken);
                if (again.IsFailure || again.Value.ResultHash != stored.ResultHash)
                {
                    await FailAsync(store, row.Id, "sim.diverged", $"The stored day {stored.Session:yyyy-MM-dd} gives another result now (a changed bar or a defect); the run cannot go on from it.", cancellationToken);
                    return null;
                }
            }

            _active = new ActiveBook(row.Id, scope, book);
            kept = true;
            return book;
        }
        finally
        {
            if (!kept)
            {
                await scope.DisposeAsync();
            }
        }
    }

    private async Task<bool> FailAsync(ISimDaysStore store, Guid id, string code, string message, CancellationToken cancellationToken)
    {
        Drop();
        SimDaysLog.Failed(logger, id, code);
        await store.FailAsync(id, (code + ": " + message)[..Math.Min(400, code.Length + 2 + message.Length)], time.GetUtcNow(), cancellationToken);
        return false;
    }

    /// <summary>With <c>Sim:AutoContinue</c> on, a complete newest run is extended once a newer session is stored (at most once a minute), through the owner's Continue command.</summary>
    private async Task AutoContinueAsync(IServiceProvider services, ISimDaysStore store, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (!options.AutoContinue || (_continueCheckedAt is { } at && now - at < ContinueEvery))
        {
            return;
        }

        _continueCheckedAt = now;
        if (await store.LatestAsync(cancellationToken) is { Phase: SimDays.Complete } latest)
        {
            await services.GetRequiredService<ICommandHandler<ContinueSimDaysCommand, SimDaysView>>().HandleAsync(new ContinueSimDaysCommand(latest.Id), cancellationToken); // nothing newer is the usual answer
        }
    }

    private void Drop()
    {
        _active?.Scope.Dispose();
        _active = null;
    }

    private sealed record ActiveBook(Guid Id, IServiceScope Scope, SimDayBook Book);
}
