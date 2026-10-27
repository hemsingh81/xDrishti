using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>
/// Writes the decision row that follows an applied proposal (spec P9-08 AC 9, 10a). The hypothesis or the basket change is already written when this runs, so a failed insert would leave a change with no decision: it is tried twice (the insert is idempotent by proposal id), and a second
/// failure is logged as an error event with the proposal id and the reference of what was written (no text, no secret) before it is thrown, so the owner can find the change.
/// </summary>
internal static class ProposalDecisionWriter
{
    public static async ValueTask<(StoredProposalDecision Decision, bool Created)> WriteAsync(IProposalDecisions decisions, ILogger logger, NewProposalDecision decision)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await decisions.InsertAsync(decision, CancellationToken.None);
            }
#pragma warning disable CA1031 // any failure of the write is retried once, then logged and rethrown
            catch (Exception) when (attempt < 2)
#pragma warning restore CA1031
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), CancellationToken.None);
            }
#pragma warning disable CA1031
            catch (Exception)
#pragma warning restore CA1031
            {
                ProposalLog.DecisionNotWritten(logger, decision.ProposalId, decision.ResultRef ?? string.Empty);
                throw;
            }
        }
    }
}
