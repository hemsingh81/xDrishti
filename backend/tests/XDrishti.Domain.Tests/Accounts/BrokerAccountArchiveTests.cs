using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class BrokerAccountArchiveTests
{
    [Fact]
    public void Archiving_keeps_the_account_and_marks_the_time()
    {
        var account = AccountData.New();
        var at = AccountData.T0.AddDays(1);

        account.Archive(at).IsSuccess.ShouldBeTrue();

        account.ArchivedAt.ShouldBe(at);
        account.Name.ShouldBe("Example One");
    }

    [Fact]
    public void The_primary_Data_account_cannot_be_archived()
    {
        var primary = AccountData.WithPrimaryData();

        var result = primary.Archive(AccountData.T0);

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        primary.ArchivedAt.ShouldBeNull();
    }

    [Fact]
    public void A_standby_can_be_archived()
    {
        var standby = AccountData.New();
        standby.ChangeRoles(AccountData.Data(DataRoleKind.Standby), null, null, AccountData.T0).IsSuccess.ShouldBeTrue();

        standby.Archive(AccountData.T0).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void An_archived_account_cannot_be_archived_again()
    {
        var account = AccountData.New();
        account.Archive(AccountData.T0);

        account.Archive(AccountData.T0.AddHours(1)).Error.ShouldBe(AccountErrors.NotFound);
    }
}
