using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Job;

/// <summary>
/// Stores a replay and queues it (SR-01 part B, AC 1, 2, 3 and 12): checks the parameters and the hold-out clamp through the driver, takes the one settings snapshot, hashes the input (parameters, snapshot, scope, strategy versions, bars), stores the run or runs as queued unless a replay
/// is active (<c>replay.busy</c>, which is also the answer while the 1-minute history backfill is incomplete) or the scope needs more memory than <c>Replay:MaxWindowMb</c> (<c>replay.invalid</c>), and records each run in the trial ledger as a look of kind <c>system_replay</c>, one row per strategy version it may use (under the as-of rule every version written before the close of its last session). A run whose ledger rows cannot be written is cancelled and the failure goes on: no run starts unrecorded. Reads stored bars only (through the clamp)
/// and writes only the replay tables and the ledger.
/// </summary>
internal sealed class StartReplayHandler(ReplayDriver driver, ReplayInputHasher hasher, IReplayInputs inputs, ISimAllBackfillGate backfill, SystemReplayOptions options, IReplayStore store, ITestLedger ledger, TimeProvider time, ILogger<StartReplayHandler> logger)
    : ICommandHandler<StartReplayCommand, StartedReplay>
{
    /// <summary>The error code of a start while a replay is queued, running or being cancelled, or while the 1-minute history backfill is incomplete (try again later).</summary>
    public const string BusyCode = "replay.busy";

    public async ValueTask<Result<StartedReplay>> HandleAsync(StartReplayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var sweep = command.SweepStarts;
        if (sweep is null ? command.Runs is not { Count: 1 or 2 } : command.Runs is not { Count: >= ReplaySweepRules.MinStarts and <= ReplaySweepRules.MaxStarts } || command.Runs.Count != sweep.Count)
        {
            return Result.Failure<StartedReplay>(Error.Validation(ReplayDriver.InvalidCode, sweep is null ? "one run, or two linked runs (frozen and learning, or selection and baseline), can be started at once" : "a sweep has one run per start date, 2 to 12"));
        }

        var sweepId = sweep is null ? (Guid?)null : Guid.CreateVersion7(time.GetUtcNow());
        var problems = new List<string>();

        var now = time.GetUtcNow();
        var rows = new List<NewReplayRun>();
        var uses = new List<(Guid RunId, ReplayParams Parameters, IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>> Versions, string InputHash)>();
        var backfillChecked = false;
        foreach (var p in command.Runs)
        {
            var prepared = await driver.PrepareAsync(p, null, null, cancellationToken);
            if (prepared.IsFailure)
            {
                if (sweep is not null && prepared.Error.Kind == ErrorKind.Validation)
                {
                    problems.Add("start " + p.From.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + ": " + prepared.Error.Message);
                    continue; // a sweep names every bad start at once
                }

                return Result.Failure<StartedReplay>(prepared.Error); // a request that cannot be used is refused (422, 409 hold-out) whatever the backfill is doing
            }

            if (sweep is not null && prepared.Value.Sessions.Count < ReplaySweepRules.MinSessions)
            {
                problems.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"start {p.From:yyyy-MM-dd}: only {prepared.Value.Sessions.Count} sessions before the end; a sweep start needs at least {ReplaySweepRules.MinSessions}"));
                continue;
            }

            if (!backfillChecked && await backfill.IncompleteReasonAsync(cancellationToken) is { } incomplete)
            {
                return Result.Failure<StartedReplay>(Error.Conflict(BusyCode, "The 1-minute history backfill is not complete (" + incomplete + "); a replay over half-loaded bars would change when it finishes. Start again when it is done."));
            }

            backfillChecked = true;

            var versions = await inputs.VersionsAsync(p, cancellationToken);
            var learning = await inputs.LearningSourcesAsync(p, cancellationToken);
            var settings = prepared.Value.Run.Settings;
            var (input, scopeHash, instruments) = await hasher.ComputeAsync(p, settings, versions, prepared.Value.Sessions, learning, cancellationToken);
            var perInstrument = (ClampedBars.MinuteLookbackDays * 5L / 7 + options.ReadAheadSessions) * 375 * 48; // bytes of bars one instrument holds in the rolling window
            if (instruments * perInstrument > options.MaxWindowMb * 1024L * 1024)
            {
                var largest = options.MaxWindowMb * 1024L * 1024 / perInstrument;
                return Result.Failure<StartedReplay>(Error.Validation(ReplayDriver.InvalidCode, $"the scope reads {instruments} instruments, which needs about {instruments * perInstrument / 1024 / 1024} MiB of bars; the limit is {options.MaxWindowMb} MiB (Replay:MaxWindowMb, raised together with the memory limit of xd-api), so the largest scope allowed is {largest} instruments"));
            }

            var id = Guid.CreateVersion7(now);
            var created = sweep is null ? now : now.AddTicks(rows.Count * 10L); // one microsecond apart: the job takes the runs of a sweep oldest first, so in date order
            rows.Add(new NewReplayRun(id, p.From, p.To, p.Mode == ReplayMode.Learning ? "learning" : "frozen", ReplayJson.Canonical(p), ReplayJson.Canonical(settings), ReplayJson.Canonical(versions), learning, input, scopeHash, prepared.Value.Sessions.Count, created, p.Arm == ReplayArm.Baseline ? "baseline" : "selection", sweepId));
            uses.Add((id, p, versions, input));
        }

        if (problems.Count > 0)
        {
            return Result.Failure<StartedReplay>(Error.Validation(ReplayDriver.InvalidCode, string.Join("; ", problems)));
        }

        Guid? pair = sweep is null && rows.Count > 1 ? Guid.CreateVersion7(now) : null;
        var newSweep = sweep is null ? null : new NewReplaySweep(sweepId!.Value, ReplayJson.Canonical(command.Runs[0] with { From = sweep[0] }), sweep, now);
        var inserted = await store.InsertAsync(pair, rows, cancellationToken, newSweep);
        if (!inserted.Started)
        {
            return Result.Failure<StartedReplay>(Error.Conflict(BusyCode, "A replay is queued or running; start another when it has finished or been cancelled."));
        }

        try
        {
            foreach (var (runId, parameters, versions, input) in uses)
            {
                foreach (var v in versions.Values.SelectMany(l => l))
                {
                    // the first half of the row id belongs to the run and the second to the version, so the ledger can count replays (one look each), not rows
                    var id = new Guid(Convert.FromHexString(ReplayJson.Sha256($"system-replay|{runId:D}")[..16] + ReplayJson.Sha256($"system-replay|{runId:D}|{v.Hash}")[..16]));
                    await ledger.AppendAsync(
                        new LedgerRow(id, input, v.StrategyId, v.Version, v.Hash, null, "system replay", parameters.From, parameters.To, LedgerRow.SystemReplayKind, now, LedgerRow.SystemReplayKind, 0),
                        cancellationToken);
                }
            }

            await store.MarkRecordedAsync([.. rows.Select(r => r.Id)], cancellationToken); // claimable only now: the rows of the ledger exist
        }
        catch
        {
            foreach (var run in rows)
            {
                await store.CancelAsync(run.Id, time.GetUtcNow(), CancellationToken.None);
            }

            throw;
        }

        foreach (var run in inserted.Runs)
        {
            ReplayLog.Started(logger, run.Id, run.SessionsTotal, run.TrialNumber);
        }

        return Result.Success(new StartedReplay([.. rows.Select(r => r.Id)], pair, inserted.Runs.Max(r => r.TrialNumber), sweepId));
    }
}
