using System.Text.Json;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The only <see cref="IMlInfluenceEvidence"/> (CF-10e parts 10e-1 and 10e-2, ADR 0281). The forward figures come from the model's graded shadow scores (<see cref="ShadowForwardFigures"/>; none: unavailable). The calibration alarm is the calibration verdict of the model's newest
/// <c>ml_monitor_checks</c> row: <c>Fires</c> true, <c>Quiet</c> false, anything else, no row, or a row older than the last closed session: null, which is unavailable and never a pass. The hold-out figures have no source yet (part 10e-3 waits for HO-01) and stay null.
/// Read only; the predicate, its bars and the owner's switch are not touched here.
/// </summary>
internal sealed class ShadowMlInfluenceEvidence(IShadowScoreReader scores, IMlMonitorChecks checks, ITradingCalendar calendars, TimeProvider clock) : IMlInfluenceEvidence
{
    public async ValueTask<MlInfluenceEvidence> ReadAsync(StoredModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(model);
        var forward = ShadowForwardFigures.Compute(await scores.OutcomesAsync(model.Id, cancellationToken));
        var alarm = await CalibrationAlarmAsync(model.Id, cancellationToken);
        // shortcut: no hold-out scoring pass exists (CF-10e part 10e-3 waits for HO-01), so the hold-out figures are unavailable
        return new MlInfluenceEvidence(null, forward, alarm);
    }

    private async ValueTask<bool?> CalibrationAlarmAsync(long modelId, CancellationToken cancellationToken)
    {
        if (await checks.LatestAsync(modelId, cancellationToken) is not { } row)
        {
            return null;
        }

        var lastClosed = (await calendars.GetAsync(cancellationToken)).LastClosedSession(clock.GetUtcNow());
        if (row.CheckedOn < lastClosed)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(row.Verdicts);
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("Kind", out var kind) && kind.GetString() == RollbackPolicy.Calibration && item.TryGetProperty("State", out var state))
                {
                    return state.GetString() switch
                    {
                        nameof(RollbackTriggerState.Fires) => true,
                        nameof(RollbackTriggerState.Quiet) => false,
                        _ => null,
                    };
                }
            }
        }
        catch (JsonException)
        {
            return null; // a row that cannot be read is unavailable, never a pass
        }

        return null;
    }
}
