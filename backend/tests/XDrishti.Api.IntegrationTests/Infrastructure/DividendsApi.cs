using XDrishti.Application.Portfolio.Dividends;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Calls of the dividends API shared by the tests (ADR 0026). Symbols and amounts are made up.</summary>
internal static class DividendsApi
{
    public static Task<HttpResponseMessage> AddDividendRawAsync(this HttpClient client, object body, CancellationToken cancellationToken) =>
        client.PostJsonAsync("/api/portfolio/dividends", body, cancellationToken);

    public static async Task<DividendView> AddDividendAsync(this HttpClient client, object body, CancellationToken cancellationToken)
    {
        using var response = await client.AddDividendRawAsync(body, cancellationToken);
        return await response.ReadAsync<DividendView>(cancellationToken);
    }

    public static async Task<DividendView> VoidDividendAsync(this HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"/api/portfolio/dividends/{id}/void", cancellationToken);
        return await response.ReadAsync<DividendView>(cancellationToken);
    }

    public static async Task<DividendPage> HoldingDividendsAsync(this HttpClient client, Guid holdingId, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/holdings/{holdingId}/dividends{query}", cancellationToken);
        return await response.ReadAsync<DividendPage>(cancellationToken);
    }

    public static async Task<DividendPage> AccountDividendsAsync(this HttpClient client, Guid accountId, string query, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/accounts/{accountId}/dividends{query}", cancellationToken);
        return await response.ReadAsync<DividendPage>(cancellationToken);
    }

    public static async Task<DividendSuggestionPage> SuggestionsAsync(this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/accounts/{accountId}/dividends/suggestions", cancellationToken);
        return await response.ReadAsync<DividendSuggestionPage>(cancellationToken);
    }

    public static Task<HttpResponseMessage> AcceptRawAsync(this HttpClient client, Guid accountId, Guid entryId, object? body, CancellationToken cancellationToken) =>
        body is null
            ? client.PostAsync($"/api/accounts/{accountId}/dividends/suggestions/{entryId}/accept", cancellationToken)
            : client.PostJsonAsync($"/api/accounts/{accountId}/dividends/suggestions/{entryId}/accept", body, cancellationToken);

    public static Task<HttpResponseMessage> DismissRawAsync(this HttpClient client, Guid accountId, Guid entryId, CancellationToken cancellationToken) =>
        client.PostAsync($"/api/accounts/{accountId}/dividends/suggestions/{entryId}/dismiss", cancellationToken);
}
