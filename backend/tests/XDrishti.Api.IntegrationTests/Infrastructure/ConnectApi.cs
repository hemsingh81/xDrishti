using System.Net;
using XDrishti.Application.Accounts.Connections;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Helpers for the connect tests: start a sign-in, follow Dhan's redirect back, read where the callback sent the browser.</summary>
internal static class ConnectApi
{
    public const string PublicBaseUrl = "http://localhost:8080";

    public static async Task<ConnectStartView> StartConnectAsync(this HttpClient client, Guid accountId, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync($"/api/accounts/{accountId}/connect", cancellationToken);
        return await response.ReadAsync<ConnectStartView>(cancellationToken);
    }

    public static Task<HttpResponseMessage> DhanCallbackAsync(this HttpClient client, string? tokenId, CancellationToken cancellationToken) =>
        client.GetAsync(tokenId is null ? "/api/brokers/dhan/callback" : $"/api/brokers/dhan/callback?tokenId={Uri.EscapeDataString(tokenId)}", cancellationToken);

    /// <summary>Where a callback redirected to (asserting a 302).</summary>
    public static string Location(this HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        return response.Headers.Location!.ToString();
    }

    public static string Connected(Guid accountId) => $"{PublicBaseUrl}/accounts?account={accountId}&connected=1";

    public static string Failed(Guid? accountId, string code) =>
        accountId is { } id ? $"{PublicBaseUrl}/accounts?account={id}&connectError={code}" : $"{PublicBaseUrl}/accounts?connectError={code}";
}
