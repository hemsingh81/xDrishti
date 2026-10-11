using XDrishti.Domain.Accounts;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class PortfolioBucketAndEventTests
{
    [Theory]
    [InlineData("Trading", true)]
    [InlineData("My Choice", true)]
    [InlineData("a_b-c9", true)]
    [InlineData("", false)]
    [InlineData(" lead", false)]
    [InlineData("a/b", false)]
    [InlineData("é", false)]
    public void Bucket_names_are_simple_text(string name, bool valid) =>
        PortfolioBucket.IsValidName(name).ShouldBe(valid);

    [Fact]
    public void A_bucket_name_is_trimmed_and_limited()
    {
        PortfolioBucket.Create("  Growth  ", AccountData.T0).Value.Name.ShouldBe("Growth");
        PortfolioBucket.Create(new string('a', 41), AccountData.T0).IsFailure.ShouldBeTrue();
        PortfolioBucket.Create(null, AccountData.T0).Error.Code.ShouldBe(AccountErrors.InvalidCode);
    }

    [Fact]
    public void An_event_records_who_when_and_what_and_limits_the_text()
    {
        var accountId = Guid.NewGuid();

        var entry = AccountEvent.Record(AccountEventType.RolesChanged, accountId, new string('d', 600), "  owner@example.invalid  ", AccountData.T0);

        entry.At.ShouldBe(AccountData.T0);
        entry.AccountId.ShouldBe(accountId);
        entry.Event.ShouldBe(AccountEventType.RolesChanged);
        entry.Actor.ShouldBe("owner@example.invalid");
        entry.Detail!.Length.ShouldBe(AccountEvent.MaxDetailLength);
    }

    [Fact]
    public void An_event_without_an_account_or_detail_is_allowed()
    {
        var entry = AccountEvent.Record(AccountEventType.BucketAdded, null, null, "owner@example.invalid", AccountData.T0);

        entry.AccountId.ShouldBeNull();
        entry.Detail.ShouldBeNull();
    }
}
