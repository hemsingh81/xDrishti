using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class PrimaryDataSwapTests
{
    private static readonly DateTimeOffset Later = AccountData.T0.AddHours(1);

    private static BrokerAccount Standby(string name = "Standby One")
    {
        var account = AccountData.New(name, "3000000003");
        account.ChangeRoles(AccountData.Data(DataRoleKind.Standby), null, null, AccountData.T0).IsSuccess.ShouldBeTrue();
        return account;
    }

    private static void Apply(PrimaryDataSwap swap)
    {
        swap.Release(Later);
        swap.Complete(Later);
    }

    [Fact]
    public void The_first_primary_needs_no_previous()
    {
        var target = AccountData.New();

        var swap = PrimaryDataSwap.Plan(target, null, null).Value;
        Apply(swap);

        swap.IsNoOp.ShouldBeFalse();
        (target.DataEnabled, target.DataPrimary, target.DataStandby).ShouldBe((true, true, false));
    }

    [Fact]
    public void Making_the_primary_primary_again_is_a_no_op()
    {
        var primary = AccountData.WithPrimaryData();

        var swap = PrimaryDataSwap.Plan(primary, primary, null).Value;

        swap.IsNoOp.ShouldBeTrue();
        swap.Previous.ShouldBeNull();
    }

    [Fact]
    public void The_standby_and_the_primary_swap_places()
    {
        var primary = AccountData.WithPrimaryData();
        var standby = Standby();

        Apply(PrimaryDataSwap.Plan(standby, primary, standby).Value);

        (standby.DataPrimary, standby.DataStandby).ShouldBe((true, false));
        (primary.DataPrimary, primary.DataStandby, primary.DataEnabled).ShouldBe((false, true, true));
    }

    [Fact]
    public void A_new_primary_pushes_the_old_one_to_standby_when_the_slot_is_free()
    {
        var primary = AccountData.WithPrimaryData();
        var target = AccountData.New("Example Two", "2000000009");

        var swap = PrimaryDataSwap.Plan(target, primary, null).Value;
        Apply(swap);

        swap.PreviousBecomesStandby.ShouldBeTrue();
        (target.DataPrimary, primary.DataPrimary, primary.DataStandby).ShouldBe((true, false, true));
    }

    [Fact]
    public void The_old_primary_loses_the_Data_role_when_another_account_already_is_the_standby()
    {
        var primary = AccountData.WithPrimaryData();
        var standby = Standby();
        var target = AccountData.New("Example Two", "2000000009");

        var swap = PrimaryDataSwap.Plan(target, primary, standby).Value;
        Apply(swap);

        swap.PreviousBecomesStandby.ShouldBeFalse();
        (primary.DataEnabled, primary.DataPrimary, primary.DataStandby).ShouldBe((false, false, false));
        (standby.DataStandby, target.DataPrimary).ShouldBe((true, true));
    }

    [Fact]
    public void After_the_release_step_nobody_holds_a_flag_so_the_database_rules_cannot_be_violated_half_way()
    {
        var primary = AccountData.WithPrimaryData();
        var standby = Standby();

        var swap = PrimaryDataSwap.Plan(standby, primary, standby).Value;
        swap.Release(Later);

        (primary.DataPrimary, primary.DataStandby, standby.DataPrimary, standby.DataStandby).ShouldBe((false, false, false, false));
    }

    [Fact]
    public void An_archived_account_cannot_become_primary()
    {
        var target = AccountData.New();
        target.Archive(AccountData.T0);

        PrimaryDataSwap.Plan(target, null, null).Error.ShouldBe(AccountErrors.NotFound);
    }

    [Fact]
    public void A_plan_without_a_current_primary_but_a_standby_target_promotes_it()
    {
        var standby = Standby();

        var swap = PrimaryDataSwap.Plan(standby, null, standby).Value;
        Apply(swap);

        (standby.DataPrimary, standby.DataStandby).ShouldBe((true, false));
        swap.Previous.ShouldBeNull();
    }
}
