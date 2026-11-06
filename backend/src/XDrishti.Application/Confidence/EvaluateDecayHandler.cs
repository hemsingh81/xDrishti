using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Paper;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Confidence;

/// <summary>
/// The daily decay monitor over every strategy (spec CF-12, part 12b, ADR 0219): the strategies that have a shadow edge candidate in the current paper epoch, each evaluated once for today by <see cref="ProposeKillCommand"/>, which is idempotent per strategy and day. One strategy that
/// fails or throws does not stop the others and records nothing for itself, so the next run repeats it; the run counts and reports the failure. With no shadow candidate there is nothing to do. With no paper epoch yet the epoch is null, exactly as in the decay report: the rows that carry no epoch
/// (written before the first epoch) are the series that is evaluated, and rows of an epoch are not. It reads shadow results only through the one reader.
/// </summary>
internal sealed class EvaluateDecayHandler(
    IShadowResultReader shadow,
    IPaperLedgerData ledger,
    ICommandHandler<ProposeKillCommand, ProposeKillOutcome> propose,
    ILogger<EvaluateDecayHandler> logger) : ICommandHandler<EvaluateDecayCommand, DecayRunOutcome>
{
    public async ValueTask<Result<DecayRunOutcome>> HandleAsync(EvaluateDecayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        // the same epoch the decay report reads (null when there is none yet: the rows written then carry no epoch)
        var epoch = await ledger.EpochAsync(null, cancellationToken);

        // shortcut: every shadow row of every epoch is read to list the strategies; ask the reader for the epoch's strategies when the record grows.
        var strategies = (await shadow.ResultsAsync(cancellationToken))
            .Where(r => r.CountsForEdge && r.EdgeEpoch == epoch?.Id && r.TradeKey.Contains('|', StringComparison.Ordinal))
            .Select(r => r.TradeKey[..r.TradeKey.IndexOf('|', StringComparison.Ordinal)])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        int evaluated = 0, already = 0, proposed = 0, applied = 0, failed = 0;
        foreach (var strategy in strategies)
        {
            Result<ProposeKillOutcome> result;
            try
            {
                result = await propose.HandleAsync(new ProposeKillCommand(strategy), cancellationToken);
            }
#pragma warning disable CA1031 // One strategy that throws (a store that is down, a refused write) must not stop the others; it is counted, logged by its type and repeated on the next run.
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
            {
                failed++;
                KillLog.StrategyThrew(logger, failure.GetType().Name);
                continue;
            }

            if (result.IsFailure)
            {
                failed++;
                KillLog.StrategyFailed(logger, result.Error.Code);
                continue;
            }

            applied += result.Value.AppliedAutomatically;
            failed += result.Value.ApplyFailures > 0 ? 1 : 0;
            if (result.Value.AlreadyChecked)
            {
                already++;
                continue;
            }

            evaluated++;
            proposed += result.Value.ProposalIds.Count;
        }

        KillLog.Run(logger, strategies.Count, evaluated, already, proposed, applied, failed);
        return Result.Success(new DecayRunOutcome(strategies.Count, evaluated, already, proposed, applied, failed));
    }
}
