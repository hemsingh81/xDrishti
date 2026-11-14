using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Lab;
using XDrishti.Application.Replay.Reads;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Sim;

/// <summary>
/// Starts a day-by-day simulation (SR-04, AC 1 and 2; ADR 0293): checks the input, applies the hold-out rule (<see cref="SimDaysHoldOut"/>: inside block 1 only once its strategy look is stored, taken automatically when free; else the end is clamped and the note says why), prepares the SR-01 run
/// (the one settings snapshot, today's versions labelled hindsight, gross), hashes the input, stores the run with its cursor on the first session and records it in the trial ledger as a <c>system_replay</c> look. A run whose ledger rows cannot be written is cancelled and the failure goes on.
/// </summary>
internal sealed class StartSimDaysHandler(
    ReplayDriver driver, IReplayFacts facts, ReplayInputHasher hasher, IReplayInputs inputs, SimDaysHoldOut holdOut, IRiskProfileStore profiles, SystemReplayOptions replayOptions, SimDaysOptions options, ISimDaysStore store, ITestLedger ledger, TimeProvider time, ILogger<StartSimDaysHandler> logger)
    : ICommandHandler<StartSimDaysCommand, Guid>
{
    public async ValueTask<Result<Guid>> HandleAsync(StartSimDaysCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var scope = new ReplayScope([.. (command.BasketIds ?? []).Distinct()], [.. (command.InstrumentIds ?? []).Distinct()]);
        if (scope.IsEmpty)
        {
            return Invalid("the scope is empty: name stocks and/or baskets");
        }

        if (scope.BasketIds.Count > ReplayRequestResolver.MaxBaskets || scope.InstrumentIds.Count > ReplayRequestResolver.MaxInstruments)
        {
            return Invalid($"at most {ReplayRequestResolver.MaxBaskets} baskets and {ReplayRequestResolver.MaxInstruments} instruments");
        }

        var capital = command.Capital ?? options.DefaultCapital;
        if (capital <= 0)
        {
            return Invalid("capital must be above 0");
        }

        ReplayMode mode;
        switch (command.Mode)
        {
            case null or "learning":
                mode = ReplayMode.Learning;
                break;
            case "frozen":
                mode = ReplayMode.Frozen;
                break;
            default:
                return Invalid("mode is learning or frozen");
        }

        if (await store.ActiveAsync(cancellationToken) is not null)
        {
            return Result.Failure<Guid>(SimDays.Busy("A simulation is active; cancel it or let it complete first."));
        }

        if (await holdOut.HistoryAsync(cancellationToken) is not { } history)
        {
            return Invalid("no bars are stored");
        }

        var calendar = await facts.CalendarAsync(cancellationToken);
        var earliest = history.First;
        for (var i = 0; i < SimDays.WarmUpSessions; i++)
        {
            earliest = SimDaysSupport.Next(calendar, earliest, DateOnly.MaxValue) ?? earliest;
        }

        var requested = command.End is { } e && e < history.LastComplete ? e : history.LastComplete;
        if (command.Start > requested)
        {
            return Invalid($"the start is after the end ({requested:yyyy-MM-dd}, the last complete stored session or the end asked for)");
        }

        if (command.Start < earliest)
        {
            return Invalid($"the start is before the first stored bar plus the warm-up of {SimDays.WarmUpSessions} sessions; the earliest start is {earliest:yyyy-MM-dd}");
        }

        var profile = ReplayRiskProfile.Standard;
        if (command.RiskProfileId is { } pid)
        {
            if (await profiles.FindAsync(pid, cancellationToken) is not { } found)
            {
                return Invalid("no such risk profile");
            }

            profile = ReplayRequestResolver.From(found);
        }
        else if ((await profiles.ListAsync(cancellationToken)).FirstOrDefault(p => p.IsSeeded && string.Equals(p.Name, "Standard", StringComparison.OrdinalIgnoreCase)) is { } standard)
        {
            profile = ReplayRequestResolver.From(standard);
        }

        var answer = await holdOut.ResolveAsync(requested, cancellationToken);
        if (answer.End < command.Start)
        {
            return Invalid("the whole range lies inside the hold-out: " + answer.Note);
        }

        if (answer.ReadThrough is { } through)
        {
            facts.ReadInsideHoldOut(through);
        }

        var p = new ReplayParams(command.Start, answer.End, scope, capital, profile, null, ReplayVersionRule.Start, mode);
        var prepared = await driver.PrepareAsync(p, null, null, cancellationToken);
        if (prepared.IsFailure)
        {
            return Result.Failure<Guid>(SimDays.Invalid(prepared.Error.Message));
        }

        if (prepared.Value.Sessions.Count == 0)
        {
            return Invalid("no trading session lies between the start and the end");
        }

        var versions = await inputs.VersionsAsync(p, cancellationToken);
        var learning = await inputs.LearningSourcesAsync(p, cancellationToken);
        var settings = prepared.Value.Run.Settings;
        var (input, _, instruments) = await hasher.ComputeAsync(p, settings, versions, prepared.Value.Sessions, learning, cancellationToken);
        if (instruments * replayOptions.BytesPerInstrument > replayOptions.MaxWindowMb * 1024L * 1024)
        {
            return Invalid($"the scope reads {instruments} instruments; the largest scope allowed is {replayOptions.MaxWindowMb * 1024L * 1024 / replayOptions.BytesPerInstrument} (Replay:MaxWindowMb)");
        }

        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7(now);
        var run = new NewSimDaysRun(
            id, input, ReplayJson.Canonical(p), ReplayJson.Canonical(settings), ReplayJson.Canonical(versions), learning, capital, mode == ReplayMode.Learning ? "learning" : "frozen", p.From, p.To, requested, prepared.Value.Sessions[0], answer.ReadThrough, answer.Note, now);
        if (!await store.InsertAsync(run, cancellationToken))
        {
            return Result.Failure<Guid>(SimDays.Busy("A simulation is active; cancel it or let it complete first."));
        }

        try
        {
            await SimDaysLedger.RecordAsync(ledger, id, input, versions, p.From, p.To, now, cancellationToken);
        }
        catch
        {
            await store.CancelAsync(id, time.GetUtcNow(), CancellationToken.None);
            throw;
        }

        SimDaysLog.Started(logger, id, p.From, p.To, answer.ReadThrough is not null);
        return Result.Success(id);
    }

    private static Result<Guid> Invalid(string message) => Result.Failure<Guid>(SimDays.Invalid(message));
}
