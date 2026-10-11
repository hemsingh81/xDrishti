using XDrishti.Application.Accounts;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts;

/// <summary>The automatic default bucket of every account that has the Portfolio role.</summary>
public sealed class DefaultBucketTests
{
    private readonly AccountsFixture _fx = new();

    private static CancellationToken Ct => CancellationToken.None;

    private Task<IReadOnlyList<BucketView>> ListAsync() => _fx.ListBuckets().HandleAsync(new ListBucketsQuery(), Ct).AsTask();

    private async Task SwitchPortfolioAsync(BrokerAccount account, bool on) =>
        (await _fx.UpdateRoles().HandleAsync(new UpdateRolesCommand(account.Id, new RolesInput(null, null, new RoleToggleInput(on)), AccountsFixture.Actor), Ct)).IsSuccess.ShouldBeTrue();

    private async Task RenameAsync(BrokerAccount account, string name) =>
        (await _fx.UpdateConnection().HandleAsync(new UpdateConnectionCommand(account.Id, Inputs.Connection(name), AccountsFixture.Actor), Ct)).IsSuccess.ShouldBeTrue();

    private PortfolioBucket BucketOf(BrokerAccount account) => _fx.Data.Buckets.Single(b => b.DefaultForAccountId == account.Id);

    [Fact]
    public async Task A_fresh_system_has_no_buckets() =>
        (await ListAsync()).ShouldBeEmpty();

    [Fact]
    public async Task Creating_an_account_with_the_portfolio_role_creates_its_default_bucket_in_the_same_save()
    {
        var created = await _fx.Create().HandleAsync(new CreateAccountCommand(Inputs.Account(roles: Inputs.Roles(false, true)), AccountsFixture.Actor), Ct);

        var bucket = _fx.Data.Buckets.ShouldHaveSingleItem();
        bucket.Name.ShouldBe("Example One · Portfolio");
        bucket.DefaultForAccountId.ShouldBe(created.Value.Id);
        created.Value.Portfolio.AutoBucketName.ShouldBe("Example One · Portfolio");
        created.Value.Portfolio.DefaultBucket.ShouldBe("Example One · Portfolio");
        _fx.Data.SaveCount.ShouldBe(1);
        var entry = _fx.Data.Events.Single(e => e.Event == AccountEventType.BucketCreated);
        entry.AccountId.ShouldBe(created.Value.Id);
        entry.Detail.ShouldBe("Default portfolio bucket 'Example One · Portfolio' created");
        entry.Actor.ShouldBe(AccountsFixture.Actor);
        (await ListAsync()).ShouldBe([new BucketView(bucket.Id, bucket.Name, true, created.Value.Id, "Example One")]);
    }

    [Fact]
    public async Task An_account_without_the_portfolio_role_has_no_bucket()
    {
        await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, false)));

        _fx.Data.Buckets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enabling_the_role_later_creates_the_bucket_and_enabling_again_never_duplicates_it()
    {
        var account = await _fx.CreatedAsync();

        await SwitchPortfolioAsync(account, true);
        await SwitchPortfolioAsync(account, true);
        await SwitchPortfolioAsync(account, false);
        await SwitchPortfolioAsync(account, true);

        BucketOf(account).Name.ShouldBe("Example One · Portfolio");
        _fx.Data.Events.Count(e => e.Event == AccountEventType.BucketCreated).ShouldBe(1);
    }

    [Fact]
    public async Task An_existing_bucket_is_reused_when_the_role_is_enabled()
    {
        var account = await _fx.CreatedAsync();
        _fx.Data.Buckets.Add(PortfolioBucket.CreateDefault(account.Id, "Example One · Portfolio", AccountsFixture.Start).Value);

        await SwitchPortfolioAsync(account, true);

        _fx.Data.Buckets.ShouldHaveSingleItem();
        _fx.Data.Events.ShouldNotContain(e => e.Event == AccountEventType.BucketCreated);
    }

    [Fact]
    public async Task A_save_refused_because_a_concurrent_request_created_the_bucket_is_tried_once_more()
    {
        var account = await _fx.CreatedAsync();
        _fx.Data.CollidingSaves = 1;

        var result = await _fx.UpdateRoles().HandleAsync(
            new UpdateRolesCommand(account.Id, new RolesInput(null, null, new RoleToggleInput(true)), AccountsFixture.Actor), Ct);

        result.IsSuccess.ShouldBeTrue();
        BucketOf(account).Name.ShouldBe("Example One · Portfolio");
        result.Value.Roles.Portfolio.Enabled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_save_refused_twice_is_a_conflict()
    {
        var account = await _fx.CreatedAsync();
        _fx.Data.CollidingSaves = 2;

        var result = await _fx.UpdateRoles().HandleAsync(
            new UpdateRolesCommand(account.Id, new RolesInput(null, null, new RoleToggleInput(true)), AccountsFixture.Actor), Ct);

        result.Error.ShouldBe(AccountErrors.ConcurrentChange);
        _fx.Data.Buckets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Renaming_the_account_renames_its_bucket_and_audits_it()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));
        var id = BucketOf(account).Id;

        await RenameAsync(account, "Dhan Main");

        var bucket = BucketOf(account);
        bucket.Id.ShouldBe(id);
        bucket.Name.ShouldBe("Dhan Main · Portfolio");
        var entry = _fx.Data.Events.Single(e => e.Event == AccountEventType.BucketRenamed);
        entry.AccountId.ShouldBe(account.Id);
        entry.Detail.ShouldBe("Default portfolio bucket 'Example One · Portfolio' renamed to 'Dhan Main · Portfolio'");
    }

    [Fact]
    public async Task Renaming_an_account_without_a_bucket_creates_none_and_a_save_without_a_name_change_renames_nothing()
    {
        var plain = await _fx.CreatedAsync();
        var withBucket = await _fx.CreatedAsync(Inputs.Account(name: "Example Two", clientId: "1000000002", roles: Inputs.Roles(false, true)));

        await RenameAsync(plain, "Renamed One");
        await RenameAsync(withBucket, "Example Two");

        _fx.Data.Buckets.ShouldHaveSingleItem().Name.ShouldBe("Example Two · Portfolio");
        _fx.Data.Events.ShouldNotContain(e => e.Event == AccountEventType.BucketRenamed);
    }

    [Fact]
    public async Task The_bucket_of_an_account_whose_role_is_off_follows_the_rename_too()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));
        await SwitchPortfolioAsync(account, false);

        await RenameAsync(account, "Renamed One");

        BucketOf(account).Name.ShouldBe("Renamed One · Portfolio");
    }

    [Fact]
    public async Task A_name_that_is_taken_gets_a_number()
    {
        var first = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));
        var second = await _fx.CreatedAsync(Inputs.Account(name: "Other", clientId: "1000000002", roles: Inputs.Roles(false, true)));
        var third = await _fx.CreatedAsync(Inputs.Account(name: "Third", clientId: "1000000003", roles: Inputs.Roles(false, true)));
        await RenameAsync(second, "Example One");
        await RenameAsync(third, "example one");

        BucketOf(first).Name.ShouldBe("Example One · Portfolio");
        BucketOf(second).Name.ShouldBe("Example One · Portfolio 2");
        BucketOf(third).Name.ShouldBe("example one · Portfolio 3");
    }

    [Fact]
    public async Task The_view_of_an_account_without_a_bucket_shows_the_name_creation_would_pick()
    {
        await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));
        var plain = await _fx.CreatedAsync(Inputs.Account(name: "Other", clientId: "1000000002"));
        await RenameAsync(plain, "Example One");

        var before = (await _fx.GetAccount().HandleAsync(new GetAccountQuery(plain.Id), Ct)).Value.Portfolio.AutoBucketName;
        await SwitchPortfolioAsync(plain, true);
        var after = (await _fx.GetAccount().HandleAsync(new GetAccountQuery(plain.Id), Ct)).Value.Portfolio.AutoBucketName;

        before.ShouldBe("Example One · Portfolio 2");
        after.ShouldBe(before);
    }

    [Fact]
    public async Task Archiving_the_account_archives_and_renames_its_bucket_and_hides_it()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));

        (await _fx.Archive().HandleAsync(new ArchiveAccountCommand(account.Id, AccountsFixture.Actor), Ct)).IsSuccess.ShouldBeTrue();

        var bucket = BucketOf(account);
        bucket.ArchivedAt.ShouldBe(AccountsFixture.Start);
        bucket.Name.ShouldBe("Example One · Portfolio (archived)");
        (await ListAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task After_archiving_the_name_is_free_for_a_new_account_with_the_same_name()
    {
        var first = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));
        await _fx.Archive().HandleAsync(new ArchiveAccountCommand(first.Id, AccountsFixture.Actor), Ct);

        var second = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));

        BucketOf(second).Name.ShouldBe("Example One · Portfolio");
        BucketOf(first).Name.ShouldBe("Example One · Portfolio (archived)");
    }

    [Fact]
    public async Task Switching_the_role_off_keeps_the_bucket_but_hides_it_until_the_role_is_back()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.Roles(false, true)));
        _fx.Data.Buckets.Add(PortfolioBucket.Create("Growth", AccountsFixture.Start).Value);

        await SwitchPortfolioAsync(account, false);
        var whileOff = await ListAsync();
        await SwitchPortfolioAsync(account, true);
        var whileOn = await ListAsync();

        whileOff.Select(b => b.Name).ShouldBe(["Growth"]);
        whileOn.Select(b => b.Name).ShouldBe(["Growth", "Example One · Portfolio"], ignoreOrder: true);
        _fx.Data.Buckets.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_custom_bucket_listing_has_no_account()
    {
        await _fx.AddBucket().HandleAsync(new AddBucketCommand(new BucketInput("Growth"), AccountsFixture.Actor), Ct);

        var listed = (await ListAsync()).Single();

        listed.IsAccountDefault.ShouldBeFalse();
        listed.AccountId.ShouldBeNull();
        listed.AccountName.ShouldBeNull();
    }

    [Fact]
    public async Task The_wizard_can_choose_the_own_bucket_by_its_name_before_it_exists()
    {
        var input = Inputs.Account(roles: Inputs.Roles(false, true), portfolio: Inputs.Portfolio("Example One · Portfolio"));

        var result = await _fx.Create().HandleAsync(new CreateAccountCommand(input, AccountsFixture.Actor), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Portfolio.DefaultBucket.ShouldBe("Example One · Portfolio");
        _fx.Data.Accounts.Single().Portfolio.DefaultBucketId.ShouldBeNull();
    }

    [Fact]
    public async Task A_custom_bucket_can_be_chosen_in_the_wizard()
    {
        _fx.Data.Buckets.Add(PortfolioBucket.Create("Growth", AccountsFixture.Start).Value);

        var result = await _fx.Create().HandleAsync(
            new CreateAccountCommand(Inputs.Account(roles: Inputs.Roles(false, true), portfolio: Inputs.Portfolio("growth")), AccountsFixture.Actor), Ct);

        result.Value.Portfolio.DefaultBucket.ShouldBe("Growth");
        result.Value.Portfolio.AutoBucketName.ShouldBe("Example One · Portfolio");
    }
}
