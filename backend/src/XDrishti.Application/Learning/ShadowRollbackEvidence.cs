using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Learning;

/// <summary>
/// The <see cref="IRollbackEvidence"/> of CF-10e part 10e-2 (ADR 0281): the champion's graded shadow scores as calibration observations (probability, and win when the R is above zero, known at the close of the candidate's session). Forward candidates are never trained on. With no
/// scored outcome the part is null (unavailable). The band trigger's cells stay null: that source is out of scope.
/// </summary>
internal sealed class ShadowRollbackEvidence(IShadowScoreReader scores) : IRollbackEvidence
{
    public async ValueTask<RollbackInputs> ReadAsync(StoredModel champion, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(champion);
        var outcomes = await scores.OutcomesAsync(champion.Id, cancellationToken);
        if (outcomes.Count == 0)
        {
            return RollbackInputs.None;
        }

        // shortcut: the known-at instant is the 15:30 close of the candidate's session (its grade's exit instant is not read here)
        var observations = outcomes.Select(o => new CalibrationObservation(
            o.TradeKey, new DateTimeOffset(o.SessionDate.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset).ToUnixTimeSeconds() + (SessionClock.CloseMinute * 60L), o.PWin, o.R > 0 ? 1 : 0)).ToList();
        return new RollbackInputs(observations, null);
    }
}
