using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The owner's decisions about Action-center items (ADR 0022). A decision is collected in the scoped context and saved by the use case with its audit event; clearing the decisions whose condition ended is one conditional statement that touches
/// only the rows still as they were read. Nothing is deleted (the runtime role cannot).
/// </summary>
internal sealed class ActionStates(XDrishtiDbContext db, IPortfolioHistoryCache cache) : IActionStates
{
    public async ValueTask<IReadOnlyList<ActionStateRow>> ListLiveAsync(CancellationToken cancellationToken) =>
        await db.ActionStates.AsNoTracking()
            .Where(x => x.State != ActionDecision.Cleared)
            .Select(x => new ActionStateRow(x.Rule, x.Key, x.State, x.SnoozedUntil, x.UpdatedAt))
            .ToListAsync(cancellationToken);

    public async ValueTask<bool> SetAsync(string rule, string key, ActionDecision state, DateOnly? snoozedUntil, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var row = await db.ActionStates.FirstOrDefaultAsync(x => x.Rule == rule && x.Key == key, cancellationToken);
        if (row is null)
        {
            if (state == ActionDecision.Cleared)
            {
                return false; // nothing was decided about this item
            }

            db.ActionStates.Add(ActionState.Create(rule, key, state, snoozedUntil, actor, now));
            return true;
        }

        return row.Decide(state, snoozedUntil, actor, now);
    }

    public async ValueTask<int> ClearDoneAsync(IReadOnlyList<ActionStateRow> rows, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return 0;
        }

        var rules = rows.Select(r => r.Rule).ToArray();
        var keys = rows.Select(r => r.Key).ToArray();
        var times = rows.Select(r => r.UpdatedAt.ToUniversalTime()).ToArray();
        var at = now.ToUniversalTime();
        var cleared = await db.Database.ExecuteSqlAsync(
            $"""
            UPDATE portfolio.action_states AS s
            SET state = 'cleared', updated_by = {actor}, updated_at = {at}
            FROM unnest({rules}::text[], {keys}::text[], {times}::timestamptz[]) AS r(r_rule, r_key, r_at)
            WHERE s.rule = r.r_rule AND s.key = r.r_key AND s.state = 'done' AND s.updated_at = r.r_at
            """,
            cancellationToken);
        if (cleared > 0)
        {
            cache.Invalidate(); // the open items (and the bell's count of them) changed
        }

        return cleared;
    }
}
