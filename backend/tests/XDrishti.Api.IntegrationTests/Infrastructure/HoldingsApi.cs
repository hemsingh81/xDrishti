using System.Globalization;
using Microsoft.EntityFrameworkCore;
using XDrishti.Application.Accounts;
using XDrishti.Application.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Calls and database helpers shared by the holdings tests. Symbols, ISINs, ids and prices are made up.</summary>
internal static class HoldingsApi
{
    public static async Task<ImportPreview> PreviewAsync(this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"/api/accounts/{accountId}/holdings/preview", cancellationToken);
        return await response.ReadAsync<ImportPreview>(cancellationToken);
    }

    /// <summary>Saves the portfolio settings (PUT) and returns the answer: the account and what the first import did.</summary>
    public static async Task<PortfolioSaved> SavePortfolioAsync(this HttpClient client, Guid accountId, object body, CancellationToken cancellationToken)
    {
        using var response = await client.PutJsonAsync($"/api/accounts/{accountId}/portfolio", body, cancellationToken);
        return await response.ReadAsync<PortfolioSaved>(cancellationToken);
    }

    /// <summary>The first import as the owner does it: saves the settings with the first import date and the rows (keys with optional dates).</summary>
    public static async Task<FirstImportView> ImportAsync(this HttpClient client, Guid accountId, string? firstImportDate, object rows, CancellationToken cancellationToken) =>
        (await client.SavePortfolioAsync(accountId, new { firstImportDate, firstImport = new { rows } }, cancellationToken)).Import!;

    public static async Task<SyncRunView> SyncAsync(this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"/api/accounts/{accountId}/holdings/sync", cancellationToken);
        return await response.ReadAsync<SyncRunView>(cancellationToken);
    }

    public static async Task<HoldingPage> ListHoldingsAsync(this HttpClient client, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/holdings" + query, cancellationToken);
        return await response.ReadAsync<HoldingPage>(cancellationToken);
    }

    public static async Task<HoldingsSummary> SummaryAsync(this HttpClient client, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("/api/holdings/summary" + query, cancellationToken);
        return await response.ReadAsync<HoldingsSummary>(cancellationToken);
    }

    public static async Task<HoldingLots> LotsAsync(this HttpClient client, Guid holdingId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/holdings/{holdingId}/lots", cancellationToken);
        return await response.ReadAsync<HoldingLots>(cancellationToken);
    }

    public static async Task<SyncStatusView> SyncStatusAsync(this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/accounts/{accountId}/holdings/sync-status", cancellationToken);
        return await response.ReadAsync<SyncStatusView>(cancellationToken);
    }

    public static async Task<Holding> HoldingAsync(this ApiFactory factory, string symbol, CancellationToken cancellationToken) =>
        await factory.WithDbAsync(db => db.Holdings.AsNoTracking().Include(h => h.Lots).SingleAsync(h => h.TradingSymbol == symbol, cancellationToken));

    public static async Task<List<HoldingSync>> SyncsAsync(this ApiFactory factory, Guid accountId, CancellationToken cancellationToken) =>
        await factory.WithDbAsync(db => db.HoldingSyncs.AsNoTracking().Where(s => s.AccountId == accountId).OrderBy(s => s.StartedAt).ThenBy(s => s.Id).ToListAsync(cancellationToken));

    public static async Task<int> CountAsync(this ApiFactory factory, string table, CancellationToken cancellationToken) =>
        await factory.WithDbAsync(async db =>
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            await db.Database.OpenConnectionAsync(cancellationToken);
            command.CommandText = $"SELECT count(*) FROM {table}";
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        });

    /// <summary>Puts an NSE equity into the instrument master with its Dhan security id (made-up values).</summary>
    public static async Task<Guid> SeedInstrumentAsync(this ApiFactory factory, string symbol, string? isin, string securityId, string displayName, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        await factory.WithDbAsync(async db =>
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, isin, series, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
                VALUES ({0}, {1}, 'NSE', 'NSE_EQ', 'share', {2}, {2}, {3}, 'EQ', 1, {1}, false, now(), now());
                INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES ({0}, 'dhan', {4}, 'NSE_EQ');
                """,
                [id, symbol, displayName, (object?)isin ?? DBNull.Value, securityId],
                cancellationToken);
            return 0;
        });
        return id;
    }
}
