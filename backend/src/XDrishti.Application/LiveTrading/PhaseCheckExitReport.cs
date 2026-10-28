using XDrishti.Application.Abstractions.LiveTrading;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.LiveTrading;

/// <summary>
/// The exit report of <c>exit_confirmed</c> (spec P11-06, ADR 0153): runs the phase check now, on the stored records, for the one live account, and says Met only when the whole report is MET. Anything else (several or no live account, a refused
/// check, NOT MET, NOT EVALUABLE, a malformed window) is not met, with the words. It reads only; the owner still records the act.
/// </summary>
internal sealed class PhaseCheckExitReport(IQueryHandler<LivePhaseCheckQuery, Result<LivePhaseCheckView>> check) : ILiveExitReport
{
    public async ValueTask<LiveExitCheck> CheckAsync(CancellationToken cancellationToken)
    {
        var result = await check.HandleAsync(new LivePhaseCheckQuery(null, null), cancellationToken);
        if (result.IsFailure)
        {
            return new LiveExitCheck(false, "the small-live phase check could not be run: " + result.Error.Message);
        }

        var report = result.Value.Report;
        return report.ExitVerdict == LiveVerdicts.Met
            ? new LiveExitCheck(true, "the small-live phase check says " + report.ExitCriterion)
            : new LiveExitCheck(false, "the small-live phase check says " + report.ExitVerdict + "; " + report.Recommendation);
    }
}
