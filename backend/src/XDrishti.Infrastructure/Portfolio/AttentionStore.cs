using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Portfolio;

/// <summary>
/// The memory of the attention items (<c>portfolio.attention_items</c>, ADR 0027). One reconciliation is three statements however many items there are: an upsert of the items that hold now (a known item keeps when it was first seen and is written only when something about it
/// changed; one that had been resolved starts anew), a single update that resolves every remembered item that no longer holds, and one read of what is shown. Nothing is deleted.
/// </summary>
internal sealed class AttentionStore(XDrishtiDbContext db) : IAttentionStore
{
    private static readonly Guid NoAccount = Guid.Empty;

    public async ValueTask<IReadOnlyList<StoredAttention>> ReconcileAsync(IReadOnlyList<AttentionDraft> current, DateTimeOffset now, TimeSpan keepResolved, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        var time = now.ToUniversalTime();
        var ids = current.Select(c => c.Id).ToArray();

        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO portfolio.attention_items AS a (id, kind, severity, title, text, actions, since, resolved_at, account_id, updated_at)
            SELECT t.id, t.kind, t.severity, t.title, t.text, t.actions::jsonb, @now, NULL, NULLIF(t.account_id, '00000000-0000-0000-0000-000000000000'::uuid), @now
            FROM unnest(@ids, @kinds, @severities, @titles, @texts, @actions, @accounts) AS t(id, kind, severity, title, text, actions, account_id)
            ON CONFLICT (id) DO UPDATE SET
                kind = EXCLUDED.kind,
                severity = EXCLUDED.severity,
                title = EXCLUDED.title,
                text = EXCLUDED.text,
                actions = EXCLUDED.actions,
                account_id = EXCLUDED.account_id,
                since = CASE WHEN a.resolved_at IS NOT NULL THEN EXCLUDED.since ELSE a.since END,
                resolved_at = NULL,
                updated_at = EXCLUDED.updated_at
            WHERE a.resolved_at IS NOT NULL
                OR (a.kind, a.severity, a.title, a.text, a.actions, a.account_id) IS DISTINCT FROM (EXCLUDED.kind, EXCLUDED.severity, EXCLUDED.title, EXCLUDED.text, EXCLUDED.actions, EXCLUDED.account_id)
            """,
            [
                new NpgsqlParameter("now", time),
                Texts("ids", ids),
                Texts("kinds", [.. current.Select(c => c.Kind)]),
                Texts("severities", [.. current.Select(c => c.Severity)]),
                Texts("titles", [.. current.Select(c => c.Title)]),
                Texts("texts", [.. current.Select(c => c.Text)]),
                Texts("actions", [.. current.Select(c => c.ActionsJson)]),
                new NpgsqlParameter("accounts", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = current.Select(c => c.AccountId ?? NoAccount).ToArray() },
            ],
            cancellationToken);

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE portfolio.attention_items SET resolved_at = @now, updated_at = @now WHERE resolved_at IS NULL AND NOT (id = ANY(@ids))",
            [new NpgsqlParameter("now", time), Texts("ids", ids)],
            cancellationToken);

        var cutoff = time - keepResolved;
        var rows = await db.AttentionItems.AsNoTracking()
            .Where(x => x.ResolvedAt == null || x.ResolvedAt > cutoff)
            .Select(x => new { x.Id, x.Kind, x.Severity, x.Title, x.Text, x.Actions, x.Since, x.ResolvedAt, x.AccountId })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(x => new StoredAttention(x.Id, x.Kind, x.Severity, x.Title, x.Text, x.Actions, x.Since, x.ResolvedAt, x.AccountId))];
    }

    private static NpgsqlParameter Texts(string name, string[] values) => new(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values };
}
