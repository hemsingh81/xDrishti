using System.Net;
using System.Net.Http.Json;
using XDrishti.Application.Accounts;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>Request bodies and small helpers shared by the accounts tests. Names, client ids and numbers are made up.</summary>
internal static class AccountsApi
{
    public const string ClientId = "1000000001";

    /// <summary>Marks every made-up secret value: it must never appear in a response, audit entry or log line.</summary>
    public const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    public const string ApiKey = "key-" + Marker;

    public const string ApiSecret = "secret-" + Marker;

    public static readonly Guid StandardProfileId = Guid.Parse("00000000-0000-7000-8000-000000000001");

    public static readonly Guid ConservativeProfileId = Guid.Parse("00000000-0000-7000-8000-000000000002");

    public static object NewAccount(
        string name = "Example One",
        string? clientId = ClientId,
        object? roles = null,
        object? trading = null,
        object? portfolio = null,
        object? data = null,
        object? secrets = null,
        string? apiKeyIssuedOn = null) =>
        new
        {
            name,
            clientId,
            secrets = secrets ?? new { apiKey = ApiKey, apiSecret = ApiSecret },
            apiKeyIssuedOn,
            color = "#1A2B3C",
            notes = "made-up account",
            roles,
            trading,
            portfolio,
            data,
        };

    public static object DataRole(string kind) => new { data = new { enabled = true, kind } };

    public static object Trading(Guid profileId, string mode = "paper", decimal amount = 500_000m, object? overrides = null) => new
    {
        mode,
        riskProfileId = profileId,
        capitalSource = "fixed",
        capitalAmount = amount,
        styles = new[] { "intraday" },
        instruments = new { intraday = new[] { "EQ_MIS", "FUT" } },
        overrides,
    };

    public static object RiskProfile(string name = "Aggressive", decimal riskPerTradePct = 1.0m) => new
    {
        name,
        riskPerTradePct,
        maxCapitalPerTradePct = 25,
        maxNewEntriesPerDay = 4,
        maxOpenPositions = 6,
        maxTotalRiskPct = 4.0m,
        maxPerSector = 2,
        maxPairCorrelation = 0.7m,
        maxIntraday = 3,
        maxSwing = 3,
        dailyLossR = 2,
        dailyLossPct = 2.0m,
        weeklyLossR = 5,
        weeklyLossAction = "halve_size",
    };

    public static async Task<AccountDetail> CreateAccountAsync(this HttpClient client, object body, CancellationToken cancellationToken)
    {
        using var response = await client.PostJsonAsync("/api/accounts", body, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
        response.Headers.Location.ShouldNotBeNull();
        return (await response.Content.ReadFromJsonAsync<AccountDetail>(cancellationToken))!;
    }

    public static async Task<AccountDetail> GetAccountAsync(this HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"/api/accounts/{id}", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<AccountDetail>(cancellationToken))!;
    }

    public static async Task<T> ReadAsCreatedAsync<T>(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(cancellationToken));
        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }
}
