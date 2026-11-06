using System.Text.Json;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The daily run of the readiness snapshots (spec CF-11 D7, ADR 0221): one insert-only snapshot per Lab strategy (at its latest version) and one for the system, each with the hash of the settings that produced it, so that a trend can be drawn and a gate decision can cite exactly
/// what was true. Idempotent per India day: a snapshot that exists is not written again. A strategy whose scorecard cannot be built is counted and repeated next time; the others go on. It stores a derived reading and nothing else: it moves no cell, raises no size and places no order.
/// </summary>
internal sealed class SnapshotScorecardsHandler(ScorecardReader reader, IReadinessSnapshotStore store, ReadinessOptions options, TimeProvider clock, ILogger<SnapshotScorecardsHandler> logger) : ICommandHandler<SnapshotScorecardsCommand, SnapshotRunOutcome>
{
    public async ValueTask<Result<SnapshotRunOutcome>> HandleAsync(SnapshotScorecardsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var today = IndiaTime.Today(clock.GetUtcNow());
        var hash = options.SnapshotHash();
        var strategies = await reader.StrategiesAsync(cancellationToken);
        int stored = 0, already = 0, failed = 0;
        var cards = new List<ScorecardView>();
        foreach (var strategy in strategies)
        {
            var version = strategy.LatestVersion > 0 ? strategy.LatestVersion : (int?)null;
            ScorecardView? card = null;
            try
            {
                card = await reader.BuildAsync(strategy.Id, version, cancellationToken);
                var id = await store.InsertAsync(new NewReadinessSnapshot("strategy", card.Strategy, card.Version, today, "daily", card.Score, card.Label, JsonSerializer.Serialize(card), hash), cancellationToken);
                if (id is null)
                {
                    already++;
                }
                else
                {
                    stored++;
                }
            }
#pragma warning disable CA1031 // One strategy that throws (a store that is down, an unreadable result, a refused insert) must not stop the others; it is counted, logged by its type and repeated on the next run.
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
            {
                failed++;
                ReadinessLog.StrategyThrew(logger, failure.GetType().Name);
            }

            // a strategy whose card could not be built is not dropped from the system score: it counts as 0 with the reason (a missing reading is never a pass)
            cards.Add(card ?? ScorecardReader.Unbuilt(strategy.Id, version, "the scorecard could not be built in this run (a store failed); it counts as 0 in the system score", options));
        }

        var system = await reader.SystemAsync(cards, cancellationToken);
        var systemId = await store.InsertAsync(new NewReadinessSnapshot("system", null, null, today, "daily", system.Score, system.Label, JsonSerializer.Serialize(system), hash), cancellationToken);
        if (systemId is null)
        {
            already++;
        }
        else
        {
            stored++;
        }

        ReadinessLog.Snapshot(logger, strategies.Count, stored, already, failed);
        return Result.Success(new SnapshotRunOutcome(strategies.Count, stored, already, failed));
    }
}
