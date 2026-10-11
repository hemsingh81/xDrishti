using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>Create, read, update and archive over HTTP against the real database.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AccountsApiTests(ApiFactory factory) : IAsyncLifetime
{
    private static readonly string[] Monthly = ["monthly"];

    private static readonly string[] OptionCodes = ["OPT"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_complete_wizard_request_creates_the_account_and_everything_reads_back()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var body = AccountsApi.NewAccount(
            roles: new { data = new { enabled = true, kind = "primary" }, trading = new { enabled = true }, portfolio = new { enabled = true } },
            trading: AccountsApi.Trading(AccountsApi.StandardProfileId, "manual_live", 750_000m, new { riskPerTradePct = 0.25m, maxNewEntriesPerDay = 2 }),
            portfolio: new { defaultBucket = "trading", historyFrom = "2024-04-01", includeInTotals = false },
            data: new { liveFeedBudget = 400 });

        using var created = await client.PostJsonAsync("/api/accounts", body, Ct);

        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var detail = (await created.Content.ReadFromJsonAsync<AccountDetail>(Ct))!;
        created.Headers.Location!.ToString().ShouldBe($"/api/accounts/{detail.Id}");
        detail.Slug.ShouldBe("example-one");
        detail.Color.ShouldBe("#1A2B3C");
        detail.MaskedClientId.ShouldBe("1000••••01");
        detail.Roles.Data.ShouldBe(new DataRoleView(true, true, false));
        detail.Roles.Trading.ShouldBe(new TradingRoleView(true, "manual_live", "Standard"));
        detail.Trading.CapitalAmount.ShouldBe(750_000m);
        detail.Trading.Styles.ShouldBe(["intraday"]);
        detail.Trading.Instruments.Intraday.ShouldBe(["EQ_MIS", "FUT"]);
        detail.Trading.Overrides.ShouldBe(new AccountOverrides(0.25m, 2, null, null));
        detail.Portfolio.ShouldBe(new PortfolioSettingsView(true, "Trading", new DateOnly(2024, 4, 1), false));
        detail.Data.LiveFeedBudget.ShouldBe(400);
        detail.TokenStatus.ShouldBe("not_connected");
        detail.Connection.ShouldBe(new ConnectionView("not_connected", null, null, null, null, null));
        detail.Secrets.Select(s => (s.Name, s.Set)).ShouldBe([("apiKey", true), ("apiSecret", true)]);
        detail.Secrets.ShouldAllBe(s => s.UpdatedAt!.Value > factory.Clock.GetUtcNow().AddMinutes(-1));
        detail.ApiKeyIssuedOn.ShouldBe(DateOnly.FromDateTime(factory.Clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime));
        detail.ApiKeyExpiresOn.ShouldBe(detail.ApiKeyIssuedOn.AddMonths(12));
        var read = await client.GetAccountAsync(detail.Id, Ct);
        read.Secrets.ShouldBe(detail.Secrets);
        read.ApiKeyExpiresOn.ShouldBe(detail.ApiKeyExpiresOn);
        read.Id.ShouldBe(detail.Id);
        read.Roles.ShouldBe(detail.Roles);
        read.Portfolio.ShouldBe(detail.Portfolio);
        read.Data.ShouldBe(detail.Data);
        read.Trading.CapitalAmount.ShouldBe(750_000m);
        read.Trading.Overrides.ShouldBe(new AccountOverrides(0.25m, 2, null, null));
    }

    [Fact]
    public async Task The_list_contains_the_summaries_in_creation_order()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var first = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("primary")), Ct);
        factory.Clock.Advance(TimeSpan.FromMilliseconds(5));
        var second = await client.CreateAccountAsync(AccountsApi.NewAccount("Example Two", "2000000002"), Ct);

        var list = await (await client.GetAsync("/api/accounts", Ct)).ReadAsync<List<AccountSummary>>(Ct);

        list.Select(a => a.Id).ShouldBe([first.Id, second.Id]);
        list[0].Roles.Data.Primary.ShouldBeTrue();
        list[1].MaskedClientId.ShouldBe("2000••••02");
        list.ShouldAllBe(a => a.TokenStatus == "not_connected" && a.Secrets.All(s => s.Set));
    }

    [Fact]
    public async Task An_empty_system_lists_nothing()
    {
        var client = (await factory.SignInAsync(Ct)).Client;

        (await (await client.GetAsync("/api/accounts", Ct)).ReadAsync<List<AccountSummary>>(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_input_returns_400_accounts_invalid_with_every_field_and_stores_nothing()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        using var response = await client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount(name: "", clientId: "x", secrets: new { apiKey = "" }, apiKeyIssuedOn: "2999-01-01"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.invalid");
        var errors = await response.ReadFieldErrorsAsync(Ct);
        errors.Keys.ShouldBe(["name", "clientId", "secrets.apiKey", "secrets.apiSecret", "apiKeyIssuedOn"], ignoreOrder: true);
        (await factory.WithDbAsync(db => db.BrokerAccounts.CountAsync(Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task Incomplete_trading_with_the_role_on_is_rejected_with_the_missing_fields()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        using var response = await client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount(roles: new { trading = new { enabled = true } }), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var errors = await response.ReadFieldErrorsAsync(Ct);
        errors.Keys.ShouldContain("roles.trading");
        errors.Keys.ShouldContain("trading.riskProfileId");
        errors.Keys.ShouldContain("trading.capitalAmount");
        errors.Keys.ShouldContain("trading.styles");
    }

    [Fact]
    public async Task Unknown_codes_and_ids_are_field_errors()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var body = AccountsApi.NewAccount(
            trading: new { mode = "auto", riskProfileId = Guid.NewGuid(), capitalSource = "wire", styles = Monthly, instruments = new { intraday = OptionCodes } },
            portfolio: new { defaultBucket = "Nope" });

        using var response = await client.PostJsonAsync("/api/accounts", body, Ct);

        var errors = await response.ReadFieldErrorsAsync(Ct);
        errors.Keys.ShouldContain("trading.mode");
        errors.Keys.ShouldContain("trading.capitalSource");
        errors.Keys.ShouldContain("trading.styles");
        errors.Keys.ShouldContain("trading.instruments.intraday");
        errors.Keys.ShouldContain("portfolio.defaultBucket");
    }

    [Fact]
    public async Task A_similar_name_is_409_accounts_conflict_and_the_same_broker_login_may_be_added_under_another_name()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        using var sameName = await client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount("example   ONE"), Ct);
        using var otherName = await client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount("Example Two"), Ct);

        sameName.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await sameName.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.conflict");
        otherName.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_Dhan_account_needs_a_client_id_and_the_broker_is_always_Dhan()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        using var missing = await client.PostJsonAsync("/api/accounts", AccountsApi.NewAccount(clientId: null), Ct);
        var created = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        (await missing.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["clientId"]);
        created.Broker.ShouldBe("dhan");
    }

    [Fact]
    public async Task Updating_the_connection_changes_the_fields_and_keeps_the_client_id_when_none_is_sent()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        var renamed = await (await client.PutJsonAsync($"/api/accounts/{account.Id}/connection",
            new { name = "Renamed One", color = "#ABCDEF", notes = "n" }, Ct)).ReadAsync<AccountDetail>(Ct);
        var newId = await (await client.PutJsonAsync($"/api/accounts/{account.Id}/connection",
            new { name = "Renamed One", clientId = "9999999999" }, Ct)).ReadAsync<AccountDetail>(Ct);

        renamed.Name.ShouldBe("Renamed One");
        renamed.Slug.ShouldBe("example-one"); // the slug is stable
        renamed.MaskedClientId.ShouldBe("1000••••01");
        renamed.Secrets.ShouldAllBe(s => s.Set);
        newId.MaskedClientId.ShouldBe("9999••••99");
    }

    [Fact]
    public async Task Trading_roles_follow_the_settings_and_settings_are_kept_when_the_role_is_off()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        using var tooEarly = await client.PutJsonAsync($"/api/accounts/{account.Id}/roles", new { trading = new { enabled = true } }, Ct);
        var configured = await (await client.PutJsonAsync($"/api/accounts/{account.Id}/trading", AccountsApi.Trading(AccountsApi.ConservativeProfileId), Ct)).ReadAsync<AccountDetail>(Ct);
        var on = await (await client.PutJsonAsync($"/api/accounts/{account.Id}/roles", new { trading = new { enabled = true } }, Ct)).ReadAsync<AccountDetail>(Ct);
        var off = await (await client.PutJsonAsync($"/api/accounts/{account.Id}/roles", new { trading = new { enabled = false } }, Ct)).ReadAsync<AccountDetail>(Ct);

        tooEarly.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        configured.Roles.Trading.Enabled.ShouldBeFalse();
        on.Roles.Trading.ShouldBe(new TradingRoleView(true, "paper", "Conservative"));
        off.Roles.Trading.Enabled.ShouldBeFalse();
        off.Trading.CapitalAmount.ShouldBe(500_000m);
        off.Trading.RiskProfileName.ShouldBe("Conservative");
    }

    [Fact]
    public async Task Out_of_range_trading_values_are_400_with_the_field_names()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        var body = AccountsApi.Trading(AccountsApi.StandardProfileId, overrides: new { riskPerTradePct = 3m, maxNewEntriesPerDay = 0, maxOpenPositions = 0, dailyLossPct = 0m });

        using var response = await client.PutJsonAsync($"/api/accounts/{account.Id}/trading", body, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(
            ["overrides.riskPerTradePct", "overrides.maxNewEntriesPerDay", "overrides.maxOpenPositions", "overrides.dailyLossPct"], ignoreOrder: true);
    }

    [Fact]
    public async Task An_unknown_risk_profile_is_a_field_error()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);

        using var response = await client.PutJsonAsync($"/api/accounts/{account.Id}/trading", AccountsApi.Trading(Guid.NewGuid()), Ct);

        (await response.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["riskProfileId"]);
    }

    [Fact]
    public async Task Portfolio_and_data_settings_need_no_step_up_and_are_validated()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        var plain = (await factory.SignInAsync(Ct)).Client;

        var portfolio = await (await plain.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { defaultBucket = "MYCHOICE", historyFrom = "2023-01-02", includeInTotals = true }, Ct)).ReadAsync<AccountDetail>(Ct);
        var data = await (await plain.PutJsonAsync($"/api/accounts/{account.Id}/data", new { liveFeedBudget = 900 }, Ct)).ReadAsync<AccountDetail>(Ct);
        using var badBucket = await plain.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { defaultBucket = "Nope" }, Ct);
        using var future = await plain.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { defaultBucket = "Trading", historyFrom = "2999-01-01" }, Ct);
        using var badBudget = await plain.PutJsonAsync($"/api/accounts/{account.Id}/data", new { liveFeedBudget = 99999 }, Ct);

        portfolio.Portfolio.ShouldBe(new PortfolioSettingsView(false, "MyChoice", new DateOnly(2023, 1, 2), true));
        data.Data.LiveFeedBudget.ShouldBe(900);
        (await badBucket.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["defaultBucket"]);
        (await future.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["historyFrom"]);
        (await badBudget.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["liveFeedBudget"]);
    }

    [Fact]
    public async Task Archiving_removes_the_account_everywhere_but_keeps_its_audit_trail_and_frees_its_name()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await client.PutJsonAsync($"/api/accounts/{account.Id}/data", new { liveFeedBudget = 10 }, Ct);

        using var archived = await client.DeleteAsync($"/api/accounts/{account.Id}", Ct);

        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/accounts", Ct).ContinueWith(t => t.Result.ReadAsync<List<AccountSummary>>(Ct), Ct).Unwrap()).ShouldBeEmpty();
        var notFound = await client.GetAsync($"/api/accounts/{account.Id}", Ct);
        notFound.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await notFound.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.not_found");
        (await client.PutJsonAsync($"/api/accounts/{account.Id}/data", new { liveFeedBudget = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/accounts/{account.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var activity = await (await client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        activity.Select(e => e.Event).ShouldBe(["AccountArchived", "DataUpdated", "AccountCreated"]);
        (await factory.WithDbAsync(db => db.BrokerAccounts.AsNoTracking().SingleAsync(a => a.Id == account.Id, Ct))).ArchivedAt.ShouldNotBeNull();

        await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct); // the same name again
    }

    [Fact]
    public async Task Every_id_based_endpoint_answers_404_for_an_unknown_account()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var id = Guid.NewGuid();
        var calls = new List<HttpResponseMessage>
        {
            await client.GetAsync($"/api/accounts/{id}", Ct),
            await client.PutJsonAsync($"/api/accounts/{id}/connection", new { name = "x y" }, Ct),
            await client.PutJsonAsync($"/api/accounts/{id}/secrets", new { apiKey = "new-key" }, Ct),
            await client.PutJsonAsync($"/api/accounts/{id}/roles", new { }, Ct),
            await client.PostJsonAsync($"/api/accounts/{id}/make-primary-data", null, Ct),
            await client.PutJsonAsync($"/api/accounts/{id}/trading", new { }, Ct),
            await client.PutJsonAsync($"/api/accounts/{id}/portfolio", new { }, Ct),
            await client.PutJsonAsync($"/api/accounts/{id}/data", new { liveFeedBudget = 1 }, Ct),
            await client.DeleteAsync($"/api/accounts/{id}", Ct),
            await client.GetAsync($"/api/accounts/{id}/activity", Ct),
        };

        foreach (var call in calls)
        {
            call.StatusCode.ShouldBe(HttpStatusCode.NotFound, call.RequestMessage?.RequestUri?.ToString());
            (await call.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.not_found");
        }
    }

    [Fact]
    public async Task Activity_lists_who_did_what_newest_first_and_honours_take()
    {
        var signedIn = await factory.SignInElevatedAsync(Ct);
        var client = signedIn.Client;
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(), Ct);
        await client.PutJsonAsync($"/api/accounts/{account.Id}/trading", AccountsApi.Trading(AccountsApi.StandardProfileId), Ct);
        await client.PutJsonAsync($"/api/accounts/{account.Id}/portfolio", new { defaultBucket = "Trading" }, Ct);

        var all = await (await client.GetAsync($"/api/accounts/{account.Id}/activity", Ct)).ReadAsync<List<AccountEventView>>(Ct);
        var latest = await (await client.GetAsync($"/api/accounts/{account.Id}/activity?take=1", Ct)).ReadAsync<List<AccountEventView>>(Ct);

        all.Select(e => e.Event).ShouldBe(["PortfolioUpdated", "TradingUpdated", "AccountCreated"]);
        all.ShouldAllBe(e => e.Actor == ApiFactory.OwnerEmail);
        all[1].Detail.ShouldBe("Trading (role off): mode paper, profile Standard, capital fixed 500000, styles intraday, no overrides");
        latest.ShouldHaveSingleItem().Event.ShouldBe("PortfolioUpdated");
    }

    [Fact]
    public async Task Archiving_a_standby_is_fine_and_the_primary_Data_account_cannot_be_archived()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var primary = await client.CreateAccountAsync(AccountsApi.NewAccount(roles: AccountsApi.DataRole("primary")), Ct);
        var standby = await client.CreateAccountAsync(AccountsApi.NewAccount("Example Two", roles: AccountsApi.DataRole("standby")), Ct);

        using var refused = await client.DeleteAsync($"/api/accounts/{primary.Id}", Ct);
        using var allowed = await client.DeleteAsync($"/api/accounts/{standby.Id}", Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.conflict");
        allowed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
