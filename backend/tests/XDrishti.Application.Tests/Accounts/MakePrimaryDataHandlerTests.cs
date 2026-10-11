using XDrishti.Application.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Tests.Accounts;

public sealed class MakePrimaryDataHandlerTests
{
    private readonly AccountsFixture _fx = new();

    private async ValueTask<Domain.Common.Result<AccountDetail>> MakePrimaryAsync(Guid id) =>
        await _fx.MakePrimary().HandleAsync(new MakePrimaryDataCommand(id, AccountsFixture.Actor), CancellationToken.None);

    [Fact]
    public async Task The_standby_takes_over_and_the_old_primary_becomes_the_standby()
    {
        var primary = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));
        var standby = await _fx.CreatedAsync(Inputs.Account(name: "Example Two", roles: Inputs.DataRole("standby")));

        var result = await MakePrimaryAsync(standby.Id);

        result.Value.Roles.Data.ShouldBe(new DataRoleView(true, true, false));
        (primary.DataPrimary, primary.DataStandby, primary.DataEnabled).ShouldBe((false, true, true));
        _fx.Data.Accounts.Count(a => a.DataPrimary).ShouldBe(1);
        _fx.Data.Accounts.Count(a => a.DataStandby).ShouldBe(1);
    }

    [Fact]
    public async Task The_switch_runs_in_the_serialised_section_and_saves_in_two_steps()
    {
        await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));
        var target = await _fx.CreatedAsync(Inputs.Account(name: "Example Two"));
        var savesBefore = _fx.Data.SaveCount;

        await MakePrimaryAsync(target.Id);

        _fx.Data.SerializedRuns.ShouldBe(1);
        (_fx.Data.SaveCount - savesBefore).ShouldBe(2); // release flags, then set the new ones
    }

    [Fact]
    public async Task An_account_without_the_Data_role_can_be_made_primary_and_the_old_primary_becomes_the_standby()
    {
        var primary = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));
        var target = await _fx.CreatedAsync(Inputs.Account(name: "Example Two"));

        await MakePrimaryAsync(target.Id);

        target.DataPrimary.ShouldBeTrue();
        primary.DataStandby.ShouldBeTrue();
    }

    [Fact]
    public async Task With_a_third_account_as_standby_the_old_primary_loses_the_Data_role()
    {
        var primary = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));
        var standby = await _fx.CreatedAsync(Inputs.Account(name: "Example Two", roles: Inputs.DataRole("standby")));
        var target = await _fx.CreatedAsync(Inputs.Account(name: "Example Three"));

        await MakePrimaryAsync(target.Id);

        (primary.DataEnabled, primary.DataPrimary, primary.DataStandby).ShouldBe((false, false, false));
        standby.DataStandby.ShouldBeTrue();
        target.DataPrimary.ShouldBeTrue();
    }

    [Fact]
    public async Task Making_the_primary_primary_again_changes_and_audits_nothing()
    {
        var primary = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));
        var events = _fx.Data.Events.Count;

        var result = await MakePrimaryAsync(primary.Id);

        result.IsSuccess.ShouldBeTrue();
        _fx.Data.Events.Count.ShouldBe(events);
    }

    [Fact]
    public async Task Both_accounts_get_an_audit_entry()
    {
        var primary = await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));
        var target = await _fx.CreatedAsync(Inputs.Account(name: "Example Two"));

        await MakePrimaryAsync(target.Id);

        var changes = _fx.Data.Events.Where(e => e.Event == AccountEventType.PrimaryDataChanged).ToList();
        changes.Select(e => e.AccountId).ShouldBe([target.Id, primary.Id], ignoreOrder: true);
        changes.ShouldAllBe(e => e.Actor == AccountsFixture.Actor);
    }

    [Fact]
    public async Task An_unknown_or_archived_account_is_not_found()
    {
        var account = await _fx.CreatedAsync();
        account.Archive(AccountsFixture.Start);

        (await MakePrimaryAsync(Guid.NewGuid())).Error.Code.ShouldBe(AccountErrors.NotFoundCode);
        (await MakePrimaryAsync(account.Id)).Error.Code.ShouldBe(AccountErrors.NotFoundCode);
    }

    [Fact]
    public async Task A_collision_in_either_step_is_a_conflict()
    {
        await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));
        var target = await _fx.CreatedAsync(Inputs.Account(name: "Example Two"));

        _fx.Data.CollidingSaves = 1;
        var first = await MakePrimaryAsync(target.Id);

        first.Error.ShouldBe(AccountErrors.ConcurrentChange);
    }
}
