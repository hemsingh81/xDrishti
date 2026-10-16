using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Calls of the broker history API shared by the tests (ADR 0025). Symbols, ids and amounts are made up.</summary>
internal static class BrokerHistoryApi
{
    public static async Task<HttpResponseMessage> SyncNowRawAsync(this HttpClient client, Guid accountId, string? from, CancellationToken cancellationToken) =>
        from is null
            ? await client.PostAsync($"/api/accounts/{accountId}/sync/broker-history", cancellationToken)
            : await client.PostJsonAsync($"/api/accounts/{accountId}/sync/broker-history", new { from }, cancellationToken);

    public static async Task<BrokerHistoryRunView> SyncNowAsync(this HttpClient client, Guid accountId, string? from, CancellationToken cancellationToken)
    {
        using var response = await client.SyncNowRawAsync(accountId, from, cancellationToken);
        return await response.ReadAsync<BrokerHistoryRunView>(cancellationToken);
    }

    public static async Task<BrokerHistoryStatusView> BrokerStatusAsync(this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/accounts/{accountId}/sync/broker-history/status", cancellationToken);
        return await response.ReadAsync<BrokerHistoryStatusView>(cancellationToken);
    }

    public static async Task<LedgerPage> LedgerAsync(this HttpClient client, Guid accountId, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/accounts/{accountId}/ledger{query}", cancellationToken);
        return await response.ReadAsync<LedgerPage>(cancellationToken);
    }

    /// <summary>Polls the status until the run that "Sync now" started has ended - the trades and, when they succeeded, the ledger - and gives that status.</summary>
    public static async Task<BrokerHistoryStatusView> WaitForTheRunAsync(this HttpClient client, Guid accountId, BrokerHistoryRunView started, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        while (true)
        {
            var status = await client.BrokerStatusAsync(accountId, timeout.Token);
            var trades = status.Trades.LastRun;
            var ended = !status.Running && trades is not null && trades.Id == started.Trades.Id && trades.Status != "running"
                && (trades.Status != "succeeded" || status.Ledger.LastRun is { StartedAt: var at, Status: not "running" } && at >= trades.StartedAt);
            if (ended)
            {
                return status;
            }

            await Task.Delay(25, timeout.Token);
        }
    }
}
