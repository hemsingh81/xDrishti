using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Domain.Market;

namespace XDrishti.Application.OptionBackfill;

/// <summary>How far the backfill is (P14-03 criterion 6): the plan for today's date set against what the fetch log says. Reads reference data and the log only - no bar.</summary>
internal sealed class BackfillProgressReader(IBackfillTargets targets, IBackfillFetchLog log, OptionBackfillSettings settings, TimeProvider clock)
{
    public async ValueTask<BackfillProgress> ReadAsync(CancellationToken cancellationToken)
    {
        var all = await targets.LoadAsync(cancellationToken);
        var states = await log.LoadStateAsync(cancellationToken);
        int planned = 0, done = 0, empty = 0, noData = 0, givenUp = 0;
        foreach (var item in BackfillPlanner.Plan(all, settings, IndiaTime.Today(clock.GetUtcNow())))
        {
            planned++;
            if (!states.TryGetValue(item.Key, out var state))
            {
                continue;
            }

            if (state.IsDone)
            {
                done++;
                empty += state.DoneOutcome == BackfillOutcomes.Empty ? 1 : 0;
                noData += state.DoneOutcome == BackfillOutcomes.NoData ? 1 : 0;
            }
            else if (state.IsGivenUp)
            {
                givenUp++;
            }
        }

        return new BackfillProgress(planned, done, empty, noData, givenUp, planned - done - givenUp);
    }
}
