using System.Text.Json;
using XDrishti.Application.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts;

public sealed class AccountQueriesTests
{
    private readonly AccountsFixture _fx = new();

    [Fact]
    public async Task The_list_shows_active_accounts_in_creation_order_with_masked_ids_and_roles()
    {
        var first = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));
        _fx.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await _fx.CreatedAsync(Inputs.Account(name: "Example Two", clientId: "ABCD123456"));
        _fx.Clock.Advance(TimeSpan.FromMinutes(1));
        var archived = await _fx.CreatedAsync(Inputs.Account(name: "Example Three"));
        archived.Archive(_fx.Clock.GetUtcNow());

        var list = await _fx.ListAccounts().HandleAsync(new ListAccountsQuery(), CancellationToken.None);

        list.Select(a => a.Id).ShouldBe([first.Id, second.Id]);
        list[0].Roles.Data.Primary.ShouldBeTrue();
        list[1].MaskedClientId.ShouldBe("ABCD••••56");
        JsonSerializer.Serialize(list).ShouldNotContain(AccountsFixture.ClientId);
        JsonSerializer.Serialize(list).ShouldNotContain("ABCD123456");
    }

    [Fact]
    public async Task The_list_says_which_secrets_are_set_per_account_and_never_their_values()
    {
        var complete = await _fx.CreatedAsync(Inputs.Account());
        _fx.Clock.Advance(TimeSpan.FromMinutes(1));
        var partial = await _fx.CreatedAsync(Inputs.Account(name: "Example Two"));
        _fx.Data.Secrets.Remove((partial.Id, BrokerSecretName.ApiSecret));

        var list = await _fx.ListAccounts().HandleAsync(new ListAccountsQuery(), CancellationToken.None);

        list.Select(a => a.Id).ShouldBe([complete.Id, partial.Id]);
        list[0].Secrets.Select(s => (s.Name, s.Set)).ShouldBe([("apiKey", true), ("apiSecret", true)]);
        list[1].Secrets.Select(s => (s.Name, s.Set)).ShouldBe([("apiKey", true), ("apiSecret", false)]);
        list[1].Secrets[1].UpdatedAt.ShouldBeNull();
        list[0].ApiKeyExpiresOn.ShouldBe(new DateOnly(2027, 10, 10));
        list.ShouldAllBe(a => a.TokenStatus == "not_connected" && a.Connection.Status == "not_connected");
        JsonSerializer.Serialize(list).ShouldNotContain(AccountsFixture.Marker);
    }

    [Fact]
    public async Task An_account_from_before_the_vault_shows_its_secrets_as_not_set()
    {
        var account = await _fx.CreatedAsync();
        _fx.Data.Secrets.Clear();

        var detail = await _fx.GetAccount().HandleAsync(new GetAccountQuery(account.Id), CancellationToken.None);

        detail.Value.Secrets.ShouldBe([new SecretStatusView("apiKey", false, null), new SecretStatusView("apiSecret", false, null)]);
    }

    [Fact]
    public async Task An_empty_list_is_fine() =>
        (await _fx.ListAccounts().HandleAsync(new ListAccountsQuery(), CancellationToken.None)).ShouldBeEmpty();

    [Fact]
    public async Task The_trading_chip_shows_the_profile_name()
    {
        await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(true, false), trading: Inputs.Trading(_fx.Conservative.Id)));

        var list = await _fx.ListAccounts().HandleAsync(new ListAccountsQuery(), CancellationToken.None);

        list.Single().Roles.Trading.ShouldBe(new TradingRoleView(true, "paper", "Conservative"));
    }

    [Fact]
    public async Task The_detail_has_all_settings_and_no_full_client_id()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(
            trading: Inputs.Trading(_fx.Standard.Id) with { Overrides = new AccountOverrides(0.25m, 2, 3, 1.0m) },
            portfolio: Inputs.Portfolio("MyChoice")));

        var result = await _fx.GetAccount().HandleAsync(new GetAccountQuery(account.Id), CancellationToken.None);

        var detail = result.Value;
        detail.Trading.Overrides.ShouldBe(new AccountOverrides(0.25m, 2, 3, 1.0m));
        detail.Trading.RiskProfileName.ShouldBe("Standard");
        detail.Portfolio.DefaultBucket.ShouldBe("MyChoice");
        detail.Data.LiveFeedBudget.ShouldBe(BrokerAccount.DefaultLiveFeedBudget);
        JsonSerializer.Serialize(detail).ShouldNotContain(AccountsFixture.ClientId);
    }

    [Fact]
    public async Task An_unknown_or_archived_account_is_not_found()
    {
        var account = await _fx.CreatedAsync();
        account.Archive(AccountsFixture.Start);

        var unknown = await _fx.GetAccount().HandleAsync(new GetAccountQuery(Guid.NewGuid()), CancellationToken.None);
        var archived = await _fx.GetAccount().HandleAsync(new GetAccountQuery(account.Id), CancellationToken.None);

        unknown.Error.Code.ShouldBe(AccountErrors.NotFoundCode);
        archived.Error.Code.ShouldBe(AccountErrors.NotFoundCode);
    }

    [Fact]
    public async Task Activity_lists_the_newest_first_and_clamps_the_page_size()
    {
        var account = await _fx.CreatedAsync();
        for (var i = 1; i <= 3; i++)
        {
            _fx.Clock.Advance(TimeSpan.FromMinutes(1));
            await _fx.UpdateData().HandleAsync(new UpdateDataCommand(account.Id, new DataInput(100 + i), AccountsFixture.Actor), CancellationToken.None);
        }

        var all = (await _fx.Activity().HandleAsync(new GetAccountActivityQuery(account.Id), CancellationToken.None)).Value;
        var one = (await _fx.Activity().HandleAsync(new GetAccountActivityQuery(account.Id, 1), CancellationToken.None)).Value;
        var tooSmall = (await _fx.Activity().HandleAsync(new GetAccountActivityQuery(account.Id, -5), CancellationToken.None)).Value;

        all.Select(e => e.Event).ShouldBe(["DataUpdated", "DataUpdated", "DataUpdated", "AccountCreated"]);
        all[0].Detail.ShouldBe("Live-feed budget 102 -> 103");
        all[0].Actor.ShouldBe(AccountsFixture.Actor);
        one.ShouldHaveSingleItem();
        tooSmall.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Activity_of_an_archived_account_stays_readable_but_an_unknown_account_is_not_found()
    {
        var account = await _fx.CreatedAsync();
        _fx.Clock.Advance(TimeSpan.FromMinutes(1));
        await _fx.Archive().HandleAsync(new ArchiveAccountCommand(account.Id, AccountsFixture.Actor), CancellationToken.None);

        var archived = await _fx.Activity().HandleAsync(new GetAccountActivityQuery(account.Id), CancellationToken.None);
        var unknown = await _fx.Activity().HandleAsync(new GetAccountActivityQuery(Guid.NewGuid()), CancellationToken.None);

        archived.Value.Select(e => e.Event).ShouldBe(["AccountArchived", "AccountCreated"]);
        unknown.Error.Code.ShouldBe(AccountErrors.NotFoundCode);
    }
}
