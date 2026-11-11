using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Job;

/// <summary>
/// Stores a replay and queues it (SR-01 part B, AC 1, 2, 3 and 12): checks the parameters and the hold-out clamp through the driver, takes the one settings snapshot, hashes the input (parameters, snapshot, scope, strategy versions, bars), stores the run or runs as queued unless a replay
/// is active (<c>replay.busy</c>), and records each run in the trial ledger as a look of kind <c>system_replay</c>, one row per strategy version it may use (under the as-of rule every version written before the close of its last session). A run whose ledger rows cannot be written is cancelled and the failure goes on: no run starts unrecorded. Reads stored bars only (through the clamp)
/// and writes only the replay tables and the ledger.
/// </summary>
internal sealed class StartReplayHandler(ReplayDriver driver, ReplayInputHasher hasher, IReplayInputs inputs, IReplayStore store, ITestLedger ledger, TimeProvider time, ILogger<StartReplayHandler> logger)
    : ICommandHandler<StartReplayCommand, StartedReplay>
{
    /// <summary>The error code of a start while a replay is queued, running or being cancelled.</summary>
    public const string BusyCode = "replay.busy";

    public async ValueTask<Result<StartedReplay>> HandleAsync(StartReplayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Runs is not { Count: 1 or 2 })
        {
            return Result.Failure<StartedReplay>(Error.Validation(ReplayDriver.InvalidCode, "one run, or a frozen and a learning run, can be started at once"));
        }

        var now = time.GetUtcNow();
        var rows = new List<NewReplayRun>();
        var uses = new List<(Guid RunId, ReplayParams Parameters, IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>> Versions, string InputHash)>();
        foreach (var p in command.Runs)
        {
            var prepared = await driver.PrepareAsync(p, null, null, cancellationToken);
            if (prepared.IsFailure)
            {
                return Result.Failure<StartedReplay>(prepared.Error);
            }

            var versions = await inputs.VersionsAsync(p, cancellationToken);
            var settings = prepared.Value.Run.Settings;
            var (input, scopeHash) = await hasher.ComputeAsync(p, settings, versions, prepared.Value.Sessions, cancellationToken);
            var id = Guid.CreateVersion7(now);
            rows.Add(new NewReplayRun(id, p.From, p.To, p.Mode == ReplayMode.Learning ? "learning" : "frozen", ReplayJson.Canonical(p), ReplayJson.Canonical(settings), ReplayJson.Canonical(versions), input, scopeHash, prepared.Value.Sessions.Count, now));
            uses.Add((id, p, versions, input));
        }

        Guid? pair = rows.Count > 1 ? Guid.CreateVersion7(now) : null;
        var inserted = await store.InsertAsync(pair, rows, cancellationToken);
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
                    var key = ReplayJson.Sha256($"system-replay|{runId:D}|{v.Hash}");
                    await ledger.AppendAsync(
                        new LedgerRow(new Guid(Convert.FromHexString(key[..32])), input, v.StrategyId, v.Version, v.Hash, null, "system replay", parameters.From, parameters.To, LedgerRow.SystemReplayKind, now, LedgerRow.SystemReplayKind, 0),
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

        return Result.Success(new StartedReplay([.. rows.Select(r => r.Id)], pair, inserted.Runs.Max(r => r.TrialNumber)));
    }
}
