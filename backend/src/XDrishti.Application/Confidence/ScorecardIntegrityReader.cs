using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Paper;
using XDrishti.Domain.Planning;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>What the scorecard knows of the hard fail "integrity fault".</summary>
/// <param name="State">Fault (criterion I NOT MET), Clear (MET), NotApplicable (no capture run yet: NOT EVALUABLE) or Unavailable (the criterion could not be read).</param>
/// <param name="Evidence">The criterion's state and why, in words, for the card.</param>
internal sealed record IntegrityReading(FaultState State, string Evidence);

/// <summary>
/// The hard fail "integrity fault" (CF-11 D3; the owner's reading of 2026-11-07, ADR 0225): the capture-integrity criterion I of the P10 paper phase check. NOT MET is a fault; MET is clear; NOT EVALUABLE (no capture run in the epoch yet) is not applicable, which is neither a pass nor a fault; a criterion
/// that cannot be read is unavailable, which counts as a fail. The criterion is read through its own narrow query, not the whole check and not a delegate (the automatic pause path can reach the scorecard through the lifecycle gate and must not reach the check's handler). The integrity
/// of the system is one fact, so it is read once per instance (a scope), however many strategies ask. Read only.
/// </summary>
internal sealed class ScorecardIntegrityReader(IQueryHandler<GetCaptureIntegrityQuery, CaptureIntegrityView> criterion, ILogger<ScorecardIntegrityReader> logger)
{
    private IntegrityReading? _reading;

    public async ValueTask<IntegrityReading> ReadAsync(CancellationToken cancellationToken) => _reading ??= await DeriveAsync(cancellationToken);

    private async ValueTask<IntegrityReading> DeriveAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Map(await criterion.HandleAsync(new GetCaptureIntegrityQuery(), cancellationToken));
        }
#pragma warning disable CA1031 // report-only: an unreadable criterion is unavailable, which counts as a fail, never a pass (cancellation still propagates)
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            ReadinessLog.IntegrityUnreadable(logger, ex.GetType().Name);
            return new IntegrityReading(FaultState.Unavailable, "unavailable, which counts as a fail: the capture-integrity criterion of the paper phase check could not be read (" + ex.GetType().Name + ").");
        }
    }

    /// <summary>The criterion's verdict as a term: NOT MET is a fault, MET is clear, NOT EVALUABLE is not applicable, any other word is unavailable.</summary>
    internal static IntegrityReading Map(CaptureIntegrityView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var detail = string.Create(CultureInfo.InvariantCulture, $"criterion I (capture integrity) of the paper phase check says {view.Verdict}: {view.Label} ({view.CaptureRuns} capture rows read).");
        return view.Verdict switch
        {
            PlanPhaseVerdicts.NotMet => new IntegrityReading(FaultState.Fault, "hard fail: " + detail),
            PlanPhaseVerdicts.Met => new IntegrityReading(FaultState.Clear, "clear: " + detail),
            PlanPhaseVerdicts.NotEvaluable => new IntegrityReading(FaultState.NotApplicable, "not applicable (no capture run in this epoch yet; neither a pass nor a fault): " + detail),
            _ => new IntegrityReading(FaultState.Unavailable, "unavailable, which counts as a fail: " + detail),
        };
    }
}
