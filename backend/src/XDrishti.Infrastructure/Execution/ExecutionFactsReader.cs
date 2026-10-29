using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Execution;
using XDrishti.Domain.Platform;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Execution;

/// <summary>Reads the execution record as the runtime role: the heartbeat row <c>exec</c>, a count of intents not in a final state, and the accounts with their stored mode. SELECT only; the role has no other right on the schema.</summary>
internal sealed class ExecutionFactsReader(XDrishtiDbContext db) : IExecutionFactsReader
{
    public async Task<ExecutionFacts> ReadAsync(CancellationToken cancellationToken)
    {
        var beat = await db.Set<ServiceHeartbeat>().AsNoTracking().Where(h => h.Id == "exec").Select(h => new { h.State, h.LastSeenAt, h.Details }).FirstOrDefaultAsync(cancellationToken);
        var open = await db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM execution.intent_state WHERE state NOT IN ('rejected', 'cancelled', 'failed', 'reconciled')").SingleAsync(cancellationToken);
        var accounts = await db.Set<XDrishti.Domain.Accounts.BrokerAccount>().AsNoTracking().Where(a => a.ArchivedAt == null).OrderBy(a => a.Name)
            .Select(a => new { a.Id, Mode = a.Trading.Mode.ToString() }).ToListAsync(cancellationToken);

        string? environment = null;
        string? hash = null;
        if (beat?.Details is { Length: > 0 } json)
        {
            using var document = JsonDocument.Parse(json);
            environment = document.RootElement.TryGetProperty("environment", out var e) ? e.GetString() : null;
            hash = document.RootElement.TryGetProperty("settingsHash", out var h) ? h.GetString() : null;
        }

        return new ExecutionFacts(beat?.State, beat?.LastSeenAt, environment, hash, open, [.. accounts.Select(a => (a.Id, Snake(a.Mode)))]);
    }

    /// <summary>The doc 20 spelling of a mode (<c>ManualLive</c> is <c>manual_live</c>).</summary>
    private static string Snake(string pascal) => string.Concat(pascal.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
}
