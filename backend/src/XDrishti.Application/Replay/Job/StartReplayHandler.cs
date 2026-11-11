using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Common;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Job;

/// <summary>
/// Stores a replay and queues it (SR-01 part B, AC 1, 2, 3 and 12): checks the parameters and the hold-out clamp through the driver, takes the one settings snapshot, hashes the input (parameters, snapshot, scope, strategy versions, bars), stores the run or runs as queued unless a replay
/// is active (<c>replay.busy</c>), and records each run in the trial ledger as a look of kind <c>system_replay</c>, one row per strategy version it uses. A run whose ledger rows cannot be written is cancelled and the failure goes on: no run starts unrecorded. Reads stored bars only (through the clamp)
/// and writes only the replay tables and the ledger.
/// </summary>
internal sealed class StartReplayHandler(ReplayDriver driver, IReplayFacts facts, IReplayBarFingerprint bars, IReplayStore store, ITestLedger ledger, TimeProvider time, ILogger<StartReplayHandler> logger)
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
        var uses = new List<(Guid RunId, ReplayParams Parameters, IReadOnlyList<VersionUse> Versions, string InputHash)>();
        foreach (var p in command.Runs)
        {
            var prepared = await driver.PrepareAsync(p, null, cancellationToken);
            if (prepared.IsFailure)
            {
                return Result.Failure<StartedReplay>(prepared.Error);
            }

            var members = (await facts.UniverseAsync(p, p.From, cancellationToken)).Members.Concat((await facts.UniverseAsync(p, p.To, cancellationToken)).Members).Select(m => m.Id).Concat(p.Scope.InstrumentIds).Distinct().Order().ToList();
            var cells = await facts.CellsAsync(p, p.From, cancellationToken);
            var versions = cells.Cells.Where(c => c.Version is not null)
                .GroupBy(c => c.Stored.Row.Cell.Strategy, StringComparer.Ordinal)
                .Select(g => new VersionUse(g.Key, g.First().Version!.Version, g.First().Version!.Hash))
                .OrderBy(v => v.StrategyId, StringComparer.Ordinal)
                .ToList();
            var scopeHash = ReplayHashes.Scope(p.Scope, members);
            var barHash = await bars.BarHashAsync(members, p.From, p.To, cancellationToken);
            var settings = prepared.Value.Run.Settings;
            var input = ReplayHashes.Input(p, settings, scopeHash, versions.ToDictionary(v => v.StrategyId, v => v.Hash, StringComparer.Ordinal), barHash);
            var id = Guid.CreateVersion7(now);
            rows.Add(new NewReplayRun(id, p.From, p.To, p.Mode == ReplayMode.Learning ? "learning" : "frozen", ReplayJson.Canonical(p), ReplayJson.Canonical(settings), input, scopeHash, prepared.Value.Sessions.Count, now));
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
                foreach (var v in versions)
                {
                    var key = ReplayJson.Sha256($"system-replay|{runId:D}|{v.Hash}");
                    await ledger.AppendAsync(
                        new LedgerRow(new Guid(Convert.FromHexString(key[..32])), input, v.StrategyId, v.Version, v.Hash, null, "system replay", parameters.From, parameters.To, LedgerRow.SystemReplayKind, now, LedgerRow.SystemReplayKind, 0),
                        cancellationToken);
                }
            }
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

    private sealed record VersionUse(string StrategyId, int Version, string Hash);
}
