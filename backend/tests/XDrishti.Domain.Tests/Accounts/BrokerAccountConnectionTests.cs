using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class BrokerAccountConnectionTests
{
    [Fact]
    public void Updating_the_connection_changes_the_fields_and_keeps_the_slug_and_the_broker()
    {
        var account = AccountData.New();
        var later = AccountData.T0.AddHours(1);

        var result = account.UpdateConnection("Renamed Account", "3000000003", "#ABCDEF", "note", later);

        result.IsSuccess.ShouldBeTrue();
        account.Name.ShouldBe("Renamed Account");
        account.Slug.ShouldBe("example-one");
        account.Broker.ShouldBe(Broker.Dhan);
        account.ClientId.ShouldBe("3000000003");
        account.Color.ShouldBe("#ABCDEF");
        account.Notes.ShouldBe("note");
        account.UpdatedAt.ShouldBe(later);
    }

    [Fact]
    public void Leaving_the_client_id_out_keeps_the_current_one()
    {
        var account = AccountData.New(clientId: "1000000001");

        account.UpdateConnection("Example One", null, null, null, AccountData.T0).IsSuccess.ShouldBeTrue();

        account.ClientId.ShouldBe("1000000001");
    }

    [Fact]
    public void An_invalid_update_changes_nothing()
    {
        var account = AccountData.New();

        var result = account.UpdateConnection("", "x", "red", null, AccountData.T0.AddHours(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Fields!.Select(f => f.Field).ShouldBe(["name", "clientId", "color"], ignoreOrder: true);
        account.Name.ShouldBe("Example One");
        account.ClientId.ShouldBe("1000000001");
        account.UpdatedAt.ShouldBe(AccountData.T0);
    }
}
