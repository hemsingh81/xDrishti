using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market.Actions;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Actions;

/// <summary>
/// Runs the detection over the whole history of every share and ETF that has day bars, now. New candidates become <c>suggested</c> actions; nothing is applied. Repeating it finds nothing new
/// (candidates with an action, ignored ones included, are not suggested again). Not audited: a suggestion is not a decision.
/// </summary>
public sealed record DetectCorporateActionsCommand : ICommand;

internal sealed class DetectCorporateActionsHandler(CorporateActionDetection detection, ICorporateActionQueries queries, ILogger<DetectCorporateActionsHandler> logger)
    : ICommandHandler<DetectCorporateActionsCommand, DetectionResult>
{
    public async ValueTask<Result<DetectionResult>> HandleAsync(DetectCorporateActionsCommand command, CancellationToken cancellationToken)
    {
        var totals = await detection.DetectAllAsync(cancellationToken);
        CorporateActionLog.DetectionRun(logger, totals.Instruments, totals.Candidates, totals.Suggested);
        var pending = await queries.GetPendingAsync(cancellationToken);
        return Result.Success(new DetectionResult(totals.Instruments, totals.Candidates, totals.Suggested, pending.Count));
    }
}
