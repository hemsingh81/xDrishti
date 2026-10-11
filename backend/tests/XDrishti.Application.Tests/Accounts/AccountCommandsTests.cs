using System.Text.Json;
using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Accounts;

/// <summary>Update connection, roles, trading, portfolio, data and archive.</summary>
public sealed class AccountCommandsTests
{
    private readonly AccountsFixture _fx = new();

    private static IEnumerable<string> Fields(Result result) => result.Error.Fields?.Select(f => f.Field) ?? [];

    private IEnumerable<AccountEvent> EventsOf(BrokerAccount account, AccountEventType type) =>
        _fx.Data.Events.Where(e => e.AccountId == account.Id && e.Event == type);

    [Fact]
    public async Task The_connection_is_updated_audited_and_the_client_id_is_never_written_to_the_audit_text()
    {
        var account = await _fx.CreatedAsync();

        var result = await _fx.UpdateConnection().HandleAsync(
            new UpdateConnectionCommand(account.Id, Inputs.Connection("Renamed One", "9999999999"), AccountsFixture.Actor), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        account.Name.ShouldBe("Renamed One");
        account.ClientId.ShouldBe("9999999999");
        var detail = EventsOf(account, AccountEventType.ConnectionUpdated).Single().Detail!;
        detail.ShouldContain("client id changed");
        detail.ShouldContain("Renamed One");
        detail.ShouldNotContain("9999999999");
        detail.ShouldNotContain(AccountsFixture.ClientId);
        JsonSerializer.Serialize(result.Value).ShouldNotContain("9999999999");
    }

    [Fact]
    public async Task Leaving_the_client_id_out_keeps_it()
    {
        var account = await _fx.CreatedAsync();

        await _fx.UpdateConnection().HandleAsync(new UpdateConnectionCommand(account.Id, Inputs.Connection("Renamed One"), AccountsFixture.Actor), CancellationToken.None);

        account.ClientId.ShouldBe(AccountsFixture.ClientId);
    }

    [Fact]
    public async Task Invalid_connection_values_and_unknown_accounts_are_refused()
    {
        var account = await _fx.CreatedAsync();

        var invalid = await _fx.UpdateConnection().HandleAsync(
            new UpdateConnectionCommand(account.Id, Inputs.Connection("", "../x"), AccountsFixture.Actor), CancellationToken.None);
        var unknown = await _fx.UpdateConnection().HandleAsync(
            new UpdateConnectionCommand(Guid.NewGuid(), Inputs.Connection(), AccountsFixture.Actor), CancellationToken.None);

        Fields(invalid).ShouldBe(["name", "clientId"], ignoreOrder: true);
        unknown.Error.Code.ShouldBe(AccountErrors.NotFoundCode);
    }

    [Fact]
    public async Task Roles_are_changed_and_the_change_is_audited_as_before_and_after()
    {
        var account = await _fx.CreatedAsync();

        var result = await _fx.UpdateRoles().HandleAsync(
            new UpdateRolesCommand(account.Id, new RolesInput(new DataRoleInput(true, "primary"), null, new RoleToggleInput(true)), AccountsFixture.Actor), CancellationToken.None);

        result.Value.Roles.Data.Primary.ShouldBeTrue();
        result.Value.Roles.Portfolio.Enabled.ShouldBeTrue();
        EventsOf(account, AccountEventType.RolesChanged).Single().Detail.ShouldBe("Roles: data off, trading off, portfolio off -> data primary, trading off, portfolio on");
    }

    [Fact]
    public async Task The_primary_cannot_lose_the_Data_role_through_the_roles_endpoint()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));

        var result = await _fx.UpdateRoles().HandleAsync(
            new UpdateRolesCommand(account.Id, new RolesInput(new DataRoleInput(false, null), null, null), AccountsFixture.Actor), CancellationToken.None);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        account.DataPrimary.ShouldBeTrue();
        EventsOf(account, AccountEventType.RolesChanged).ShouldBeEmpty();
    }

    [Fact]
    public async Task Roles_of_an_unknown_account_or_with_an_unknown_kind_are_refused()
    {
        var account = await _fx.CreatedAsync();

        var unknown = await _fx.UpdateRoles().HandleAsync(new UpdateRolesCommand(Guid.NewGuid(), new RolesInput(null, null, null), AccountsFixture.Actor), CancellationToken.None);
        var badKind = await _fx.UpdateRoles().HandleAsync(
            new UpdateRolesCommand(account.Id, new RolesInput(new DataRoleInput(true, "main"), null, null), AccountsFixture.Actor), CancellationToken.None);

        unknown.Error.Code.ShouldBe(AccountErrors.NotFoundCode);
        Fields(badKind).ShouldContain("roles.data.kind");
    }

    [Fact]
    public async Task Trading_is_replaced_validated_and_audited_without_secrets()
    {
        var account = await _fx.CreatedAsync();
        var input = new TradingInput(
            "manual_live", _fx.Standard.Id, "fixed", 750_000m, [AccountCodes.Intraday, AccountCodes.Swing],
            new InstrumentsInput(["EQ_MIS"], ["EQ_CNC", "FUT"], ["FUT"]), new AccountOverrides(0.25m, null, 3, null));

        var result = await _fx.UpdateTrading().HandleAsync(new UpdateTradingCommand(account.Id, input, AccountsFixture.Actor), CancellationToken.None);

        var trading = result.Value.Trading;
        trading.Mode.ShouldBe("manual_live");
        trading.Styles.ShouldBe(["intraday", "swing"]);
        trading.Instruments.SwingBuy.ShouldBe(["EQ_CNC", "FUT"]);
        trading.Overrides.ShouldBe(new AccountOverrides(0.25m, null, 3, null));
        EventsOf(account, AccountEventType.TradingUpdated).Single().Detail.ShouldBe("Trading (role off): mode manual_live, profile Standard, capital fixed 750000, styles intraday+swing, with overrides");
    }

    [Fact]
    public async Task Trading_with_an_unknown_profile_or_out_of_range_values_is_invalid()
    {
        var account = await _fx.CreatedAsync();

        var unknownProfile = await _fx.UpdateTrading().HandleAsync(new UpdateTradingCommand(account.Id, Inputs.Trading(Guid.NewGuid()), AccountsFixture.Actor), CancellationToken.None);
        var outOfRange = await _fx.UpdateTrading().HandleAsync(
            new UpdateTradingCommand(account.Id, Inputs.Trading(_fx.Standard.Id) with { Overrides = new AccountOverrides(5m, 0, null, null) }, AccountsFixture.Actor), CancellationToken.None);

        Fields(unknownProfile).ShouldContain("riskProfileId");
        Fields(outOfRange).ShouldBe(["overrides.riskPerTradePct", "overrides.maxNewEntriesPerDay"], ignoreOrder: true);
        EventsOf(account, AccountEventType.TradingUpdated).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_enabled_trading_role_cannot_be_made_incomplete()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(true, false), trading: Inputs.Trading(_fx.Standard.Id)));

        var result = await _fx.UpdateTrading().HandleAsync(
            new UpdateTradingCommand(account.Id, Inputs.Trading(_fx.Standard.Id, amount: null), AccountsFixture.Actor), CancellationToken.None);

        Fields(result).ShouldContain("capitalAmount");
        account.Trading.CapitalAmount.ShouldBe(500_000m);
    }

    [Fact]
    public async Task Portfolio_settings_use_the_stored_spelling_of_a_custom_bucket()
    {
        var account = await _fx.CreatedAsync();
        await AddCustomBucketAsync("MyChoice");

        var result = await _fx.UpdatePortfolio().HandleAsync(
            new UpdatePortfolioCommand(account.Id, Inputs.Portfolio("mychoice", includeInTotals: false), AccountsFixture.Actor), CancellationToken.None);

        result.Value.Account.Portfolio.DefaultBucket.ShouldBe("MyChoice");
        account.Portfolio.DefaultBucketId.ShouldBe(_fx.Data.Buckets.Single(b => b.Name == "MyChoice").Id);
        EventsOf(account, AccountEventType.PortfolioUpdated).Single().Detail.ShouldBe("Portfolio (role off): imports go to bucket MyChoice, excluded from totals");
    }

    [Fact]
    public async Task Without_a_chosen_bucket_imports_go_to_the_accounts_own_default_bucket()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(name: "Dhan · Main".Replace("·", "-"), roles: Inputs.Roles(false, true)));

        var result = await _fx.UpdatePortfolio().HandleAsync(
            new UpdatePortfolioCommand(account.Id, new PortfolioInput(null, null), AccountsFixture.Actor), CancellationToken.None);

        result.Value.Account.Portfolio.DefaultBucket.ShouldBe("Dhan - Main · Portfolio");
        result.Value.Account.Portfolio.AutoBucketName.ShouldBe("Dhan - Main · Portfolio");
        account.Portfolio.DefaultBucketId.ShouldBeNull();
        account.Portfolio.IncludeInTotals.ShouldBeTrue();
        EventsOf(account, AccountEventType.PortfolioUpdated).Single().Detail.ShouldBe("Portfolio (role on): imports go to the account's own bucket, included in totals");
    }

    [Theory]
    [InlineData("Example One · Portfolio")]
    [InlineData("example one · portfolio")]
    public async Task Choosing_the_accounts_own_default_bucket_stores_no_custom_bucket(string name)
    {
        var account = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));
        await AddCustomBucketAsync("Growth");
        await _fx.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(account.Id, Inputs.Portfolio("Growth"), AccountsFixture.Actor), CancellationToken.None);

        var result = await _fx.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(account.Id, Inputs.Portfolio(name), AccountsFixture.Actor), CancellationToken.None);

        result.Value.Account.Portfolio.DefaultBucket.ShouldBe("Example One · Portfolio");
        account.Portfolio.DefaultBucketId.ShouldBeNull();
    }

    [Fact]
    public async Task The_default_bucket_of_another_account_or_an_unknown_bucket_is_invalid()
    {
        var account = await _fx.CreatedAsync();
        await _fx.CreatedAsync(Inputs.Account(name: "Example Two", clientId: "1000000002", roles: Inputs.Roles(false, true)));

        var unknown = await _fx.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(account.Id, Inputs.Portfolio("Nope"), AccountsFixture.Actor), CancellationToken.None);
        var foreign = await _fx.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(account.Id, Inputs.Portfolio("Example Two · Portfolio"), AccountsFixture.Actor), CancellationToken.None);

        Fields(unknown).ShouldBe(["defaultBucket"]);
        Fields(foreign).ShouldBe(["defaultBucket"]);
        account.Portfolio.DefaultBucketId.ShouldBeNull();
        EventsOf(account, AccountEventType.PortfolioUpdated).ShouldBeEmpty();
    }

    private async Task AddCustomBucketAsync(string name) =>
        (await _fx.AddBucket().HandleAsync(new AddBucketCommand(new BucketInput(name), AccountsFixture.Actor), CancellationToken.None)).IsSuccess.ShouldBeTrue();

    [Fact]
    public async Task The_live_feed_budget_is_changed_and_range_checked()
    {
        var account = await _fx.CreatedAsync();

        var ok = await _fx.UpdateData().HandleAsync(new UpdateDataCommand(account.Id, new DataInput(900), AccountsFixture.Actor), CancellationToken.None);
        var tooBig = await _fx.UpdateData().HandleAsync(new UpdateDataCommand(account.Id, new DataInput(9000), AccountsFixture.Actor), CancellationToken.None);
        var missing = await _fx.UpdateData().HandleAsync(new UpdateDataCommand(account.Id, new DataInput(null), AccountsFixture.Actor), CancellationToken.None);

        ok.Value.Data.LiveFeedBudget.ShouldBe(900);
        Fields(tooBig).ShouldContain("liveFeedBudget");
        Fields(missing).ShouldContain("liveFeedBudget");
    }

    [Fact]
    public async Task Archiving_removes_the_account_from_the_list_and_keeps_its_events()
    {
        var account = await _fx.CreatedAsync();

        var result = await _fx.Archive().HandleAsync(new ArchiveAccountCommand(account.Id, AccountsFixture.Actor), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        (await _fx.ListAccounts().HandleAsync(new ListAccountsQuery(), CancellationToken.None)).ShouldBeEmpty();
        _fx.Data.Secrets.Keys.ShouldNotContain(k => k.AccountId == account.Id);
        EventsOf(account, AccountEventType.AccountArchived).ShouldHaveSingleItem().Detail!.ShouldContain("stored secrets deleted");
        EventsOf(account, AccountEventType.AccountCreated).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_primary_Data_account_cannot_be_archived_and_a_second_archive_is_not_found()
    {
        var primary = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));
        var other = await _fx.CreatedAsync(Inputs.Account(name: "Example Two"));

        var refused = await _fx.Archive().HandleAsync(new ArchiveAccountCommand(primary.Id, AccountsFixture.Actor), CancellationToken.None);
        await _fx.Archive().HandleAsync(new ArchiveAccountCommand(other.Id, AccountsFixture.Actor), CancellationToken.None);
        var again = await _fx.Archive().HandleAsync(new ArchiveAccountCommand(other.Id, AccountsFixture.Actor), CancellationToken.None);

        refused.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        again.Error.Code.ShouldBe(AccountErrors.NotFoundCode);
        primary.ArchivedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Every_change_is_audited_with_the_actor()
    {
        var account = await _fx.CreatedAsync();
        await _fx.UpdateConnection().HandleAsync(new UpdateConnectionCommand(account.Id, Inputs.Connection(), AccountsFixture.Actor), CancellationToken.None);
        await _fx.UpdateTrading().HandleAsync(new UpdateTradingCommand(account.Id, Inputs.Trading(_fx.Standard.Id), AccountsFixture.Actor), CancellationToken.None);
        await _fx.UpdatePortfolio().HandleAsync(new UpdatePortfolioCommand(account.Id, Inputs.Portfolio(), AccountsFixture.Actor), CancellationToken.None);
        await _fx.UpdateData().HandleAsync(new UpdateDataCommand(account.Id, new DataInput(10), AccountsFixture.Actor), CancellationToken.None);
        await _fx.UpdateRoles().HandleAsync(new UpdateRolesCommand(account.Id, Inputs.DataRole("standby"), AccountsFixture.Actor), CancellationToken.None);

        _fx.Data.Events.Select(e => e.Event).ShouldBe(
            [AccountEventType.AccountCreated, AccountEventType.ConnectionUpdated, AccountEventType.TradingUpdated, AccountEventType.PortfolioUpdated, AccountEventType.DataUpdated, AccountEventType.RolesChanged]);
        _fx.Data.Events.ShouldAllBe(e => e.Actor == AccountsFixture.Actor);
    }

    [Fact]
    public async Task A_save_that_collides_returns_a_conflict_for_every_command()
    {
        var account = await _fx.CreatedAsync();
        _fx.Data.CollidingSaves = 4;

        var connection = await _fx.UpdateConnection().HandleAsync(new UpdateConnectionCommand(account.Id, Inputs.Connection("Other"), AccountsFixture.Actor), CancellationToken.None);
        var roles = await _fx.UpdateRoles().HandleAsync(new UpdateRolesCommand(account.Id, Inputs.DataRole("standby"), AccountsFixture.Actor), CancellationToken.None);
        var data = await _fx.UpdateData().HandleAsync(new UpdateDataCommand(account.Id, new DataInput(5), AccountsFixture.Actor), CancellationToken.None);
        var archive = await _fx.Archive().HandleAsync(new ArchiveAccountCommand(account.Id, AccountsFixture.Actor), CancellationToken.None);

        connection.Error.ShouldBe(AccountErrors.ConcurrentChange);
        roles.Error.ShouldBe(AccountErrors.ConcurrentChange);
        data.Error.ShouldBe(AccountErrors.ConcurrentChange);
        archive.Error.ShouldBe(AccountErrors.ConcurrentChange);
    }
}
