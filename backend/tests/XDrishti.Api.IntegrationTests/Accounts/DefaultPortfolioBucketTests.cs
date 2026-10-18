using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>The automatic default bucket of every Portfolio account, over HTTP and on the real database.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class DefaultPortfolioBucketTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public async ValueTask DisposeAsync() => await factory.ResetAccountsAsync();

    private static readonly object PortfolioOn = new { portfolio = new { enabled = true } };

    private static readonly object PortfolioOff = new { portfolio = new { enabled = false } };

    private static async Task<List<BucketView>> ListAsync(HttpClient client) =>
        await (await client.GetAsync("/api/portfolio-buckets", Ct)).ReadAsync<List<BucketView>>(Ct);

    private static async Task<HttpResponseMessage> RolesAsync(HttpClient client, Guid id, object roles) =>
        await client.PutJsonAsync($"/api/accounts/{id}/roles", roles, Ct);

    private Task<List<Domain.Accounts.PortfolioBucket>> BucketRowsAsync() =>
        factory.WithDbAsync(db => db.PortfolioBuckets.AsNoTracking().OrderBy(b => b.Name).ToListAsync(Ct));

    [Fact]
    public async Task Creating_an_account_with_the_portfolio_role_creates_its_bucket_and_lists_it()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        var account = await client.CreateAccountAsync(AccountsApi.NewAccount("Dhan · Main", roles: PortfolioOn), Ct);

        account.Portfolio.ShouldBe(new PortfolioSettingsView(true, "Dhan · Main · Portfolio", "Dhan · Main · Portfolio", true, "off", "16:00", new DateOnly(2025, 1, 1), false));
        var listed = await ListAsync(client);
        listed.ShouldBe([new BucketView(listed.Single().Id, "Dhan · Main · Portfolio", true, account.Id, "Dhan · Main")]);
        var row = (await BucketRowsAsync()).Single();
        row.DefaultForAccountId.ShouldBe(account.Id);
        row.ArchivedAt.ShouldBeNull();
        var activity = await (await client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        activity.Select(e => e.Event).ShouldBe(["BucketCreated", "AccountCreated"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_fresh_database_has_no_buckets_and_an_account_without_the_role_adds_none()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        (await ListAsync(client)).ShouldBeEmpty();

        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        (await ListAsync(client)).ShouldBeEmpty();
        (await BucketRowsAsync()).ShouldBeEmpty();
        account.Portfolio.AutoBucketName.ShouldBe("Example One · Portfolio"); // what will be created
    }

    [Fact]
    public async Task Enabling_the_role_creates_the_bucket_once_and_switching_it_off_hides_it_without_deleting_it()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        (await RolesAsync(client, account.Id, PortfolioOn)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RolesAsync(client, account.Id, PortfolioOn)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var whileOn = await ListAsync(client);
        (await RolesAsync(client, account.Id, PortfolioOff)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var whileOff = await ListAsync(client);
        var kept = (await BucketRowsAsync()).Single();
        (await RolesAsync(client, account.Id, PortfolioOn)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var backOn = await ListAsync(client);

        whileOn.Single().IsAccountDefault.ShouldBeTrue();
        whileOff.ShouldBeEmpty();
        backOn.Single().Id.ShouldBe(kept.Id);
        (await BucketRowsAsync()).Single().Id.ShouldBe(kept.Id);
    }

    [Fact]
    public async Task Renaming_the_account_renames_the_bucket_and_keeps_its_id()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: PortfolioOn), Ct);
        var before = (await BucketRowsAsync()).Single();

        var renamed = await (await client.PutJsonAsync($"/api/accounts/{account.Id}/connection", new { name = "Dhan · Long Term" }, Ct)).ReadAsync<AccountDetail>(Ct);

        renamed.Portfolio.AutoBucketName.ShouldBe("Dhan · Long Term · Portfolio");
        var after = (await BucketRowsAsync()).Single();
        after.Id.ShouldBe(before.Id);
        after.Name.ShouldBe("Dhan · Long Term · Portfolio");
        var activity = await (await client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        activity.ShouldContain(e => e.Event == "BucketRenamed" && e.Detail == "Default portfolio bucket 'Example One · Portfolio' renamed to 'Dhan · Long Term · Portfolio'");
    }

    [Fact]
    public async Task Archiving_the_account_archives_and_renames_its_bucket_and_a_new_account_with_the_same_name_gets_the_plain_name()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var first = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: PortfolioOn), Ct);

        (await client.DeleteAsync($"/api/accounts/{first.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var archived = (await BucketRowsAsync()).Single();
        var second = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: PortfolioOn), Ct);

        archived.ArchivedAt.ShouldNotBeNull();
        archived.Name.ShouldBe("Example One · Portfolio (archived)");
        second.Portfolio.AutoBucketName.ShouldBe("Example One · Portfolio");
        (await ListAsync(client)).Select(b => b.Name).ShouldBe(["Example One · Portfolio"]);
    }

    [Fact]
    public async Task A_custom_or_the_own_bucket_can_be_chosen_and_another_accounts_bucket_cannot()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: PortfolioOn), Ct);
        await client.CreateAccountAsync(AccountsApi.NewAccount("Example Two", "1000000002", roles: PortfolioOn), Ct);
        (await client.PostJsonAsync("/api/portfolio-buckets", new { name = "Growth" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);

        var custom = (await (await client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { defaultBucket = "growth" }, Ct)).ReadAsync<PortfolioSaved>(Ct)).Account;
        using var foreign = await client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { defaultBucket = "Example Two · Portfolio" }, Ct);
        var own = (await (await client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { defaultBucket = "Example One · Portfolio" }, Ct)).ReadAsync<PortfolioSaved>(Ct)).Account;
        var omitted = (await (await client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { includeInTotals = false }, Ct)).ReadAsync<PortfolioSaved>(Ct)).Account;

        custom.Portfolio.DefaultBucket.ShouldBe("Growth");
        custom.Portfolio.AutoBucketName.ShouldBe("Example One · Portfolio");
        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await foreign.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["defaultBucket"]);
        own.Portfolio.DefaultBucket.ShouldBe("Example One · Portfolio");
        omitted.Portfolio.ShouldBe(new PortfolioSettingsView(true, "Example One · Portfolio", "Example One · Portfolio", false, "off", "16:00", new DateOnly(2025, 1, 1), false));
        var listed = await ListAsync(client);
        listed.Count.ShouldBe(3);
        listed.Single(b => b.Name == "Growth").IsAccountDefault.ShouldBeFalse();
        listed.Where(b => b.IsAccountDefault).Select(b => b.AccountName).ShouldBe(["Example One", "Example Two"], ignoreOrder: true);
    }

    [Fact]
    public async Task Two_simultaneous_requests_enabling_the_role_leave_exactly_one_bucket()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        for (var round = 0; round < 5; round++)
        {
            var account = await client.CreateAccountAsync(AccountsApi.NewAccount($"Racer {round}", $"10000001{round:00}"), Ct);

            var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => RolesAsync(client, account.Id, PortfolioOn)));

            // A request that lost to another one is refused with 409 (or succeeds with nothing to do); none fails, none duplicates the bucket.
            responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
            responses.ShouldContain(r => r.StatusCode == HttpStatusCode.OK);
            (await BucketRowsAsync()).Count(b => b.DefaultForAccountId == account.Id).ShouldBe(1);
            (await client.GetAccountAsync(account.Id, Ct)).Roles.Portfolio.Enabled.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Two_accounts_created_with_clashing_bucket_names_never_share_a_name()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        // "Alpha" and "alpha" have the same slug, so the second one is a similar-name conflict; names that differ only by spaces clash on the bucket name.
        var first = await client.CreateAccountAsync(AccountsApi.NewAccount("Alpha", roles: PortfolioOn), Ct);
        await client.PutJsonAsync($"/api/accounts/{first.Id}/connection", new { name = "Beta" }, Ct);
        var second = await client.CreateAccountAsync(AccountsApi.NewAccount("Beta Two", "1000000002", roles: PortfolioOn), Ct);
        await client.PutJsonAsync($"/api/accounts/{second.Id}/connection", new { name = "Beta" }, Ct);

        var names = (await BucketRowsAsync()).Select(b => b.Name).ToList();

        names.ShouldBe(["Beta · Portfolio", "Beta · Portfolio 2"], ignoreOrder: true);
    }
}
