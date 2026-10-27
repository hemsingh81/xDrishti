using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;

namespace XDrishti.Application.Ai.Drafting;

/// <summary>
/// Writes the decision row that follows a stored strategy (spec P9-07). The strategy is already in the library when this runs, so a failed insert would leave a version with no decision: it is tried twice (the insert is idempotent by draft id), and a second failure is logged as an error
/// event with the draft id and the stored strategy and version (no text, no secret) before it is thrown, so the owner can find the version and the draft is not confirmed twice (its id is now taken).
/// </summary>
internal static class DecisionWriter
{
    public static async ValueTask<(StoredDraftDecision Decision, bool Created)> WriteAsync(IDraftStore store, ILogger logger, NewDraftDecision decision)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await store.InsertDecisionAsync(decision, CancellationToken.None);
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
                DraftLog.DecisionNotWritten(logger, decision.DraftId, decision.StrategyId ?? string.Empty, decision.StrategyVersion ?? 0);
                throw;
            }
        }
    }
}
