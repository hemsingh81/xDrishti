using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts.RiskProfiles;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>
/// Concurrent requests must never break the Data role rules. Each request comes from its own signed-in, elevated client
/// (own address, so the sign-in rate limit does not interfere) and they are released together.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class AccountRaceTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public async ValueTask DisposeAsync() => await factory.ResetAccountsAsync();

    private async Task<HttpClient[]> ClientsAsync(int count)
    {
        var clients = new HttpClient[count];
        for (var i = 0; i < count; i++)
        {
            clients[i] = (await factory.SignInElevatedAsync(Ct)).Client;
        }

        return clients;
    }

    private static async Task<HttpResponseMessage[]> InParallelAsync(IEnumerable<Func<Task<HttpResponseMessage>>> calls)
    {
        using var gate = new ManualResetEventSlim(false);
        var tasks = calls.Select(call => Task.Run(() =>
        {
            gate.Wait(Ct);
            return call();
        }, Ct)).ToArray();
        gate.Set();
        return await Task.WhenAll(tasks);
    }

    private async Task<(int Primaries, int Standbys, int Both)> CountsAsync() =>
        await factory.WithDbAsync(async db => (
            await db.BrokerAccounts.CountAsync(a => a.DataPrimary && a.ArchivedAt == null, Ct),
            await db.BrokerAccounts.CountAsync(a => a.DataStandby && a.ArchivedAt == null, Ct),
            await db.BrokerAccounts.CountAsync(a => a.DataPrimary && a.DataStandby, Ct)));

    [Fact]
    public async Task Concurrent_make_primary_calls_always_end_with_exactly_one_primary()
    {
        var admin = (await factory.SignInElevatedAsync(Ct)).Client;
        var a = await admin.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("primary")), Ct);
        var b = await admin.CreateAccountAsync(AccountsApi.NewAccount("Example Two", roles: AccountsApi.DataRole("standby")), Ct);
        var c = await admin.CreateAccountAsync(AccountsApi.NewAccount("Example Three"), Ct);
        var d = await admin.CreateAccountAsync(AccountsApi.NewAccount("Example Four"), Ct);
        var ids = new[] { a.Id, b.Id, c.Id, d.Id };
        var clients = await ClientsAsync(ids.Length * 2);

        for (var round = 0; round < 4; round++)
        {
            var calls = clients.Select((client, i) => (Func<Task<HttpResponseMessage>>)(() => client.PostJsonAsync($"/api/accounts/{ids[(i + round) % ids.Length]}/make-primary-data", null, Ct)));

            var responses = await InParallelAsync(calls);

            responses.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
            responses.ShouldContain(r => r.StatusCode == HttpStatusCode.OK);
            var (primaries, standbys, both) = await CountsAsync();
            primaries.ShouldBe(1, $"round {round}");
            standbys.ShouldBeLessThanOrEqualTo(1, $"round {round}");
            both.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Two_concurrent_creations_that_both_want_to_be_primary_leave_exactly_one_primary()
    {
        var clients = await ClientsAsync(2);
        var calls = clients.Select((client, i) => (Func<Task<HttpResponseMessage>>)(() =>
            client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount($"Racer {i}", roles: AccountsApi.DataRole("primary")), Ct)));

        var responses = await InParallelAsync(calls);

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(1);
        (await CountsAsync()).Primaries.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_creations_with_the_same_name_create_one_account_and_conflict_for_the_rest()
    {
        var clients = await ClientsAsync(4);
        var calls = clients.Select((client, i) => (Func<Task<HttpResponseMessage>>)(() =>
            client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount("Same Name"), Ct)));

        var responses = await InParallelAsync(calls);

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(3);
        (await factory.WithDbAsync(db => db.BrokerAccounts.CountAsync(Ct))).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_role_changes_to_primary_on_different_accounts_leave_one_primary()
    {
        var admin = (await factory.SignInElevatedAsync(Ct)).Client;
        var accounts = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            accounts.Add((await admin.CreateAccountAsync(AccountsApi.NewAccount($"Role Racer {i}"), Ct)).Id);
        }

        var clients = await ClientsAsync(accounts.Count);
        var calls = clients.Select((client, i) => (Func<Task<HttpResponseMessage>>)(() =>
            client.PutJsonAsync($"/api/accounts/{accounts[i]}/roles", AccountsApi.DataRole("primary"), Ct)));

        var responses = await InParallelAsync(calls);

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBe(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).ShouldBe(accounts.Count - 1);
        (await CountsAsync()).Primaries.ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_a_profile_while_an_account_switches_to_it_never_leaves_a_trading_account_without_a_profile()
    {
        var admin = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await admin.CreateAccountAsync(
            AccountsApi.NewAccount(roles: new { trading = new { enabled = true } }, trading: AccountsApi.Trading(AccountsApi.StandardProfileId)), Ct);
        var clients = await ClientsAsync(2);

        for (var round = 0; round < 6; round++)
        {
            var profile = await (await admin.PostJsonAsync("/api/risk-profiles", AccountsApi.RiskProfile($"Contested {round}"), Ct)).ReadAsCreatedAsync<RiskProfileView>(Ct);

            var responses = await InParallelAsync([
                () => clients[0].DeleteAsync($"/api/risk-profiles/{profile.Id}", Ct),
                () => clients[1].PutJsonAsync($"/api/accounts/{account.Id}/trading", AccountsApi.Trading(profile.Id), Ct),
            ]);

            var (deleted, switched) = (responses[0].StatusCode, responses[1].StatusCode);
            var outcome = $"round {round}: delete {deleted}, switch {switched}";
            (deleted == HttpStatusCode.NoContent ^ switched == HttpStatusCode.OK).ShouldBeTrue(outcome);
            var orphans = await factory.WithDbAsync(db => db.BrokerAccounts.CountAsync(a => a.ArchivedAt == null && a.Trading.Enabled && a.Trading.RiskProfileId == null, Ct));
            orphans.ShouldBe(0, outcome);
            if (switched == HttpStatusCode.OK)
            {
                using var reset = await admin.PutJsonAsync($"/api/accounts/{account.Id}/trading", AccountsApi.Trading(AccountsApi.StandardProfileId), Ct);
                reset.StatusCode.ShouldBe(HttpStatusCode.OK, outcome);
            }
        }
    }
}
