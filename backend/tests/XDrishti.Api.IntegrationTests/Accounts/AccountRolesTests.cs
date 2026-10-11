using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>The Data role rules and "make primary" over HTTP.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AccountRolesTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<AccountDetail> RolesAsync(HttpClient client, Guid id, object roles) =>
        await (await client.PutJsonAsync($"/api/accounts/{id}/roles", roles, Ct)).ReadAsync<AccountDetail>(Ct);

    [Fact]
    public async Task A_second_primary_or_a_second_standby_is_409_and_the_kind_is_required()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        await client.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("primary")), Ct);
        var standby = await client.CreateAccountAsync(AccountsApi.NewAccount("Example Two", roles: AccountsApi.DataRole("standby")), Ct);
        var third = await client.CreateAccountAsync(AccountsApi.NewAccount("Example Three"), Ct);

        using var secondPrimary = await client.PutJsonAsync($"/api/accounts/{third.Id}/roles", AccountsApi.DataRole("primary"), Ct);
        using var secondStandby = await client.PutJsonAsync($"/api/accounts/{third.Id}/roles", AccountsApi.DataRole("standby"), Ct);
        using var noKind = await client.PutJsonAsync($"/api/accounts/{third.Id}/roles", new { data = new { enabled = true } }, Ct);
        using var badKind = await client.PutJsonAsync($"/api/accounts/{third.Id}/roles", AccountsApi.DataRole("main"), Ct);

        secondPrimary.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await secondPrimary.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.conflict");
        secondStandby.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        noKind.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noKind.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["roles.data.kind"]);
        badKind.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAccountAsync(standby.Id, Ct)).Roles.Data.Standby.ShouldBeTrue();
    }

    [Fact]
    public async Task The_primary_cannot_lose_its_role_or_become_standby_until_another_account_is_primary()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var primary = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("primary")), Ct);

        using var off = await client.PutJsonAsync($"/api/accounts/{primary.Id}/roles", new { data = new { enabled = false } }, Ct);
        using var toStandby = await client.PutJsonAsync($"/api/accounts/{primary.Id}/roles", AccountsApi.DataRole("standby"), Ct);

        off.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        toStandby.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await client.GetAccountAsync(primary.Id, Ct)).Roles.Data.Primary.ShouldBeTrue();
    }

    [Fact]
    public async Task A_standby_can_be_switched_off_and_keeps_its_live_feed_budget()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var standby = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("standby"), data: new { liveFeedBudget = 750 }), Ct);

        var off = await RolesAsync(client, standby.Id, new { data = new { enabled = false } });

        off.Roles.Data.ShouldBe(new DataRoleView(false, false, false));
        off.Data.LiveFeedBudget.ShouldBe(750);
    }

    [Fact]
    public async Task Make_primary_swaps_with_the_standby_and_audits_both_accounts()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var primary = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("primary")), Ct);
        var standby = await client.CreateAccountAsync(AccountsApi.NewAccount("Example Two", roles: AccountsApi.DataRole("standby")), Ct);

        var result = await (await client.PostJsonAsync($"/api/accounts/{standby.Id}/make-primary-data", null, Ct)).ReadAsync<AccountDetail>(Ct);

        result.Roles.Data.ShouldBe(new DataRoleView(true, true, false));
        (await client.GetAccountAsync(primary.Id, Ct)).Roles.Data.ShouldBe(new DataRoleView(true, false, true));
        foreach (var id in new[] { primary.Id, standby.Id })
        {
            var activity = await (await client.GetAsync($"/api/accounts/{id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
            activity.ShouldContain(e => e.Event == "PrimaryDataChanged");
        }

        await AssertExactlyOnePrimaryAsync();
    }

    [Fact]
    public async Task Making_an_account_without_the_Data_role_primary_pushes_the_old_primary_to_standby_or_off()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var primary = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("primary")), Ct);
        var second = await client.CreateAccountAsync(AccountsApi.NewAccount("Example Two"), Ct);
        var third = await client.CreateAccountAsync(AccountsApi.NewAccount("Example Three"), Ct);

        await client.PostJsonAsync($"/api/accounts/{second.Id}/make-primary-data", null, Ct); // old primary becomes standby
        (await client.GetAccountAsync(primary.Id, Ct)).Roles.Data.Standby.ShouldBeTrue();
        await client.PostJsonAsync($"/api/accounts/{third.Id}/make-primary-data", null, Ct); // standby slot is taken: the old primary loses the role

        (await client.GetAccountAsync(third.Id, Ct)).Roles.Data.Primary.ShouldBeTrue();
        (await client.GetAccountAsync(second.Id, Ct)).Roles.Data.ShouldBe(new DataRoleView(false, false, false));
        (await client.GetAccountAsync(primary.Id, Ct)).Roles.Data.Standby.ShouldBeTrue();
        await AssertExactlyOnePrimaryAsync();
    }

    [Fact]
    public async Task Making_the_primary_primary_again_is_fine_and_changes_nothing()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var primary = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("primary")), Ct);

        var again = await (await client.PostJsonAsync($"/api/accounts/{primary.Id}/make-primary-data", null, Ct)).ReadAsync<AccountDetail>(Ct);

        again.Roles.Data.Primary.ShouldBeTrue();
        (await client.GetAsync($"/api/accounts/{primary.Id}/activity", Ct).ContinueWith(t => t.Result.ReadAsync<List<AccountEventView>>(Ct), Ct).Unwrap()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_primary_can_be_archived_once_another_account_has_taken_over()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var primary = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("primary")), Ct);
        var other = await client.CreateAccountAsync(AccountsApi.NewAccount("Example Two"), Ct);
        (await client.DeleteAsync($"/api/accounts/{primary.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await client.PostJsonAsync($"/api/accounts/{other.Id}/make-primary-data", null, Ct);
        await client.PutJsonAsync($"/api/accounts/{primary.Id}/roles", new { data = new { enabled = false } }, Ct);

        (await client.DeleteAsync($"/api/accounts/{primary.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await AssertExactlyOnePrimaryAsync();
    }

    [Fact]
    public async Task Portfolio_needs_a_default_bucket_before_the_role_can_be_switched_on()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        using var tooEarly = await client.PutJsonAsync($"/api/accounts/{account.Id}/roles", new { portfolio = new { enabled = true } }, Ct);
        await client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { defaultBucket = "MyLongTerm" }, Ct);
        var on = await RolesAsync(client, account.Id, new { portfolio = new { enabled = true } });

        tooEarly.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooEarly.ReadFieldErrorsAsync(Ct)).Keys.ShouldContain("portfolio.defaultBucket");
        on.Roles.Portfolio.Enabled.ShouldBeTrue();
    }

    private async Task AssertExactlyOnePrimaryAsync()
    {
        var (primaries, standbys) = await factory.WithDbAsync(async db => (
            await db.BrokerAccounts.CountAsync(a => a.DataPrimary && a.ArchivedAt == null, Ct),
            await db.BrokerAccounts.CountAsync(a => a.DataStandby && a.ArchivedAt == null, Ct)));
        primaries.ShouldBe(1);
        standbys.ShouldBeLessThanOrEqualTo(1);
    }
}
