using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Tests.Accounts;

/// <summary>The Data role rules: one primary, at most one standby, the primary cannot lose its role.</summary>
public sealed class BrokerAccountRolesTests
{
    private static Result ChangeRoles(BrokerAccount account, RolesChange change, BrokerAccount? primary = null, BrokerAccount? standby = null) =>
        account.ChangeRoles(change, primary, standby, AccountData.T0.AddMinutes(1));

    [Fact]
    public void An_account_can_become_the_primary_Data_account_when_there_is_none()
    {
        var account = AccountData.New();

        ChangeRoles(account, AccountData.Data(DataRoleKind.Primary)).IsSuccess.ShouldBeTrue();

        (account.DataEnabled, account.DataPrimary, account.DataStandby).ShouldBe((true, true, false));
    }

    [Fact]
    public void A_second_primary_is_a_conflict_that_points_to_make_primary()
    {
        var existing = AccountData.WithPrimaryData();
        var account = AccountData.New("Example Two", "2000000002");

        var result = ChangeRoles(account, AccountData.Data(DataRoleKind.Primary), existing);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        result.Error.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Message.ShouldContain("make primary");
        account.DataEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Setting_the_same_primary_again_is_fine()
    {
        var account = AccountData.WithPrimaryData();

        ChangeRoles(account, AccountData.Data(DataRoleKind.Primary), account).IsSuccess.ShouldBeTrue();

        account.DataPrimary.ShouldBeTrue();
    }

    [Fact]
    public void An_account_can_become_the_standby_when_the_slot_is_free()
    {
        var primary = AccountData.WithPrimaryData();
        var account = AccountData.New("Example Two", "2000000002");

        ChangeRoles(account, AccountData.Data(DataRoleKind.Standby), primary).IsSuccess.ShouldBeTrue();

        (account.DataEnabled, account.DataPrimary, account.DataStandby).ShouldBe((true, false, true));
    }

    [Fact]
    public void A_second_standby_is_a_conflict()
    {
        var standby = AccountData.New("Standby One", "2000000003");
        ChangeRoles(standby, AccountData.Data(DataRoleKind.Standby)).IsSuccess.ShouldBeTrue();
        var account = AccountData.New("Example Two", "2000000002");

        var result = ChangeRoles(account, AccountData.Data(DataRoleKind.Standby), null, standby);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        account.DataStandby.ShouldBeFalse();
    }

    [Fact]
    public void The_primary_cannot_become_standby()
    {
        var primary = AccountData.WithPrimaryData();

        var result = ChangeRoles(primary, AccountData.Data(DataRoleKind.Standby), primary);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        primary.DataPrimary.ShouldBeTrue();
    }

    [Fact]
    public void The_primary_cannot_lose_the_Data_role()
    {
        var primary = AccountData.WithPrimaryData();

        var result = ChangeRoles(primary, AccountData.DataOff(), primary);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        (primary.DataEnabled, primary.DataPrimary).ShouldBe((true, true));
    }

    [Fact]
    public void A_standby_can_lose_the_Data_role_and_keeps_its_live_feed_budget()
    {
        var standby = AccountData.New();
        ChangeRoles(standby, AccountData.Data(DataRoleKind.Standby)).IsSuccess.ShouldBeTrue();
        standby.ConfigureData(750, AccountData.T0).IsSuccess.ShouldBeTrue();

        ChangeRoles(standby, AccountData.DataOff(), null, standby).IsSuccess.ShouldBeTrue();

        (standby.DataEnabled, standby.DataStandby).ShouldBe((false, false));
        standby.LiveFeedBudget.ShouldBe(750);
    }

    [Fact]
    public void Enabling_Data_without_a_kind_is_a_field_error()
    {
        var account = AccountData.New();

        var result = ChangeRoles(account, new RolesChange(new DataRoleChange(true, null), null, null));

        result.Error.Code.ShouldBe(AccountErrors.InvalidCode);
        result.Error.Fields.ShouldNotBeNull().ShouldContain(f => f.Field == "roles.data.kind");
    }

    [Fact]
    public void Trading_cannot_be_switched_on_before_it_is_complete()
    {
        var account = AccountData.New();

        var result = ChangeRoles(account, new RolesChange(null, true, null));

        result.Error.Code.ShouldBe(AccountErrors.InvalidCode);
        var fields = result.Error.Fields!.Select(f => f.Field).ToList();
        fields.ShouldContain("roles.trading");
        fields.ShouldContain("trading.riskProfileId");
        fields.ShouldContain("trading.capitalAmount");
        fields.ShouldContain("trading.styles");
        account.Trading.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void Trading_can_be_switched_on_and_off_and_keeps_its_settings()
    {
        var account = AccountData.New();
        account.ConfigureTrading(AccountData.CompleteTrading(), AccountData.T0).IsSuccess.ShouldBeTrue();

        ChangeRoles(account, new RolesChange(null, true, null)).IsSuccess.ShouldBeTrue();
        account.Trading.Enabled.ShouldBeTrue();
        ChangeRoles(account, new RolesChange(null, false, null)).IsSuccess.ShouldBeTrue();

        account.Trading.Enabled.ShouldBeFalse();
        account.Trading.RiskProfileId.ShouldBe(AccountData.ProfileId);
        account.Trading.CapitalAmount.ShouldBe(500_000m);
    }

    [Fact]
    public void Portfolio_needs_a_default_bucket_before_it_is_switched_on()
    {
        var account = AccountData.New();

        var result = ChangeRoles(account, new RolesChange(null, null, true));

        result.Error.Fields!.Select(f => f.Field).ShouldContain("portfolio.defaultBucket");
        account.Portfolio.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void A_role_change_is_all_or_nothing()
    {
        var account = AccountData.New();

        var result = ChangeRoles(account, new RolesChange(new DataRoleChange(true, DataRoleKind.Primary), true, null));

        result.IsFailure.ShouldBeTrue(); // trading is incomplete
        account.DataEnabled.ShouldBeFalse();
        account.Trading.Enabled.ShouldBeFalse();
    }

    [Fact]
    public void A_change_that_names_no_role_changes_nothing_but_succeeds()
    {
        var account = AccountData.New();

        ChangeRoles(account, new RolesChange(null, null, null)).IsSuccess.ShouldBeTrue();

        account.DataEnabled.ShouldBeFalse();
    }
}
