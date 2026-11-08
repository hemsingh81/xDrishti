using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>
/// Row 15's P3-09 term (spec CF-11, ADR 0226): the owner's newest record of a <c>verify-pack --gate</c> result for the strategy version. A PASS over at least 20 trades is a pass; any other status is a fail; no record, no version or a record that cannot be read is
/// unavailable, which counts as a fail. Never a pass by default. Read only.
/// </summary>
internal sealed class PackGateResultReader(IPackGateResultReader results, ILogger<PackGateResultReader> logger)
{
    public async ValueTask<ReadinessReading<bool>> ReadAsync(string strategy, int? version, CancellationToken cancellationToken)
    {
        if (version is null)
        {
            return Unavailable("the strategy has no version, so there is no pack gate record for it.");
        }

        try
        {
            var row = await results.LatestAsync(strategy, version.Value, cancellationToken);
            if (row is null)
            {
                return Unavailable($"the owner has recorded no verify-pack --gate result for version {version} of this strategy.");
            }

            return Map(row);
        }
#pragma warning disable CA1031 // report-only: an unreadable record is unavailable, which counts as a fail, never a pass (cancellation still propagates)
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            ReadinessLog.PackGateUnreadable(logger, ex.GetType().Name);
            return Unavailable("the owner's verify-pack --gate record could not be read (" + ex.GetType().Name + ").");
        }
    }

    internal static ReadinessReading<bool> Map(PackGateResultRow row)
    {
        var note = $"newest owner record: pack {row.PackId}, {row.Status}, {row.TradeCount} trade(s).";
        return PackGateStatuses.Counts(row.Status, row.TradeCount) ? new ReadinessReading<bool>(true, note) : new ReadinessReading<bool>(false, note);
    }

    private static ReadinessReading<bool> Unavailable(string reason) => new(null, "unavailable: " + reason);
}
