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
    public void A_custom_bucket_belongs_to_no_account()
    {
        var bucket = PortfolioBucket.Create("Growth", AccountData.T0).Value;

        bucket.IsAccountDefault.ShouldBeFalse();
        bucket.DefaultForAccountId.ShouldBeNull();
        bucket.ArchivedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData("Dhan · Main", "Dhan · Main · Portfolio")]
    [InlineData("  Example One ", "Example One · Portfolio")]
    public void The_default_bucket_is_named_after_the_account(string accountName, string expected) =>
        PortfolioBucket.DefaultNameFor(accountName).ShouldBe(expected);

    [Fact]
    public void A_default_bucket_is_linked_to_its_account_and_may_use_the_dot_in_its_name()
    {
        var accountId = Guid.NewGuid();

        var bucket = PortfolioBucket.CreateDefault(accountId, "Dhan · Main · Portfolio", AccountData.T0).Value;

        bucket.IsAccountDefault.ShouldBeTrue();
        bucket.DefaultForAccountId.ShouldBe(accountId);
        bucket.Name.ShouldBe("Dhan · Main · Portfolio");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" padded ")]
    [InlineData("line\nbreak")]
    public void A_default_bucket_name_must_be_clean(string? name) =>
        PortfolioBucket.CreateDefault(Guid.NewGuid(), name, AccountData.T0).Error.Code.ShouldBe(AccountErrors.InvalidCode);

    [Fact]
    public void A_default_bucket_name_is_limited_to_the_stored_length()
    {
        PortfolioBucket.CreateDefault(Guid.NewGuid(), new string('a', PortfolioBucket.MaxStoredNameLength), AccountData.T0).IsSuccess.ShouldBeTrue();
        PortfolioBucket.CreateDefault(Guid.NewGuid(), new string('a', PortfolioBucket.MaxStoredNameLength + 1), AccountData.T0).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_default_bucket_is_renamed_and_a_custom_one_is_not()
    {
        var defaultBucket = PortfolioBucket.CreateDefault(Guid.NewGuid(), "Old · Portfolio", AccountData.T0).Value;
        var custom = PortfolioBucket.Create("Growth", AccountData.T0).Value;

        defaultBucket.Rename("New · Portfolio").IsSuccess.ShouldBeTrue();
        custom.Rename("Other").Error.Code.ShouldBe(AccountErrors.ConflictCode);

        defaultBucket.Name.ShouldBe("New · Portfolio");
        custom.Name.ShouldBe("Growth");
    }

    [Fact]
    public void Archiving_marks_the_time_once_and_frees_the_name()
    {
        var bucket = PortfolioBucket.CreateDefault(Guid.NewGuid(), "A · Portfolio", AccountData.T0).Value;

        bucket.Archive(AccountData.T0.AddDays(1), _ => false);
        bucket.Archive(AccountData.T0.AddDays(2), _ => false);

        bucket.ArchivedAt.ShouldBe(AccountData.T0.AddDays(1));
        bucket.Name.ShouldBe("A · Portfolio (archived)");
    }

    [Fact]
    public void An_archived_name_that_is_taken_gets_a_number()
    {
        var bucket = PortfolioBucket.CreateDefault(Guid.NewGuid(), "A · Portfolio", AccountData.T0).Value;

        bucket.Archive(AccountData.T0, n => n == "A · Portfolio (archived)");

        bucket.Name.ShouldBe("A · Portfolio (archived) 2");
    }

    [Fact]
    public void An_archived_name_stays_within_the_stored_length()
    {
        var bucket = PortfolioBucket.CreateDefault(Guid.NewGuid(), new string('a', PortfolioBucket.MaxStoredNameLength), AccountData.T0).Value;

        bucket.Archive(AccountData.T0, n => n.Length == bucket.ArchivedBaseName().Length);

        bucket.Name.Length.ShouldBeLessThanOrEqualTo(PortfolioBucket.MaxStoredNameLength);
        bucket.Name.ShouldEndWith("(archived) 2");
    }

    [Fact]
    public void The_free_default_name_looks_at_the_existing_names_ignoring_case()
    {
        PortfolioBucket.FreeDefaultName("A", []).ShouldBe("A · Portfolio");
        PortfolioBucket.FreeDefaultName("A", ["a · PORTFOLIO"]).ShouldBe("A · Portfolio 2");
        PortfolioBucket.FreeDefaultName("A", ["A · Portfolio", "A · Portfolio 2"]).ShouldBe("A · Portfolio 3");
    }

    [Fact]
    public void A_free_name_is_kept_and_a_taken_one_gets_a_number()
    {
        PortfolioBucket.FreeName("A · Portfolio", _ => false).ShouldBe("A · Portfolio");
        PortfolioBucket.FreeName("A · Portfolio", n => n == "A · Portfolio").ShouldBe("A · Portfolio 2");
        PortfolioBucket.FreeName("A · Portfolio", n => n is "A · Portfolio" or "A · Portfolio 2").ShouldBe("A · Portfolio 3");
    }

    [Fact]
    public void No_name_is_returned_when_every_numbered_one_is_taken() =>
        PortfolioBucket.FreeName("A · Portfolio", _ => true).ShouldBeNull();

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
