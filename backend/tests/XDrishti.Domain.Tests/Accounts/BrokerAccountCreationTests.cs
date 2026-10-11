using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Tests.Accounts;

public sealed class BrokerAccountCreationTests
{
    private static Result<BrokerAccount> Create(
        string? name = "Example One", string? clientId = "1000000001", string? color = null, string? notes = null, DateOnly? issuedOn = null) =>
        BrokerAccount.Create(name, clientId, issuedOn, color, notes, AccountData.Today, AccountData.T0);

    private static IReadOnlyList<FieldError> FieldsOf(Result result) => result.Error.Fields ?? [];

    [Fact]
    public void A_new_account_has_clean_defaults_and_every_role_off()
    {
        var account = Create(name: "  Dhan · Main  ", notes: " first ").Value;

        account.Name.ShouldBe("Dhan · Main");
        account.Slug.ShouldBe("dhan-main");
        account.Color.ShouldBe(BrokerAccount.DefaultColor);
        account.Notes.ShouldBe("first");
        account.CreatedAt.ShouldBe(AccountData.T0);
        account.ArchivedAt.ShouldBeNull();
        account.DataEnabled.ShouldBeFalse();
        account.DataPrimary.ShouldBeFalse();
        account.DataStandby.ShouldBeFalse();
        account.LiveFeedBudget.ShouldBe(BrokerAccount.DefaultLiveFeedBudget);
        account.Trading.Enabled.ShouldBeFalse();
        account.Trading.Mode.ShouldBe(TradingMode.Paper);
        account.Portfolio.Enabled.ShouldBeFalse();
        account.Portfolio.IncludeInTotals.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Dhan · Main", "dhan-main")]
    [InlineData("  My   Long--Term!! ", "my-long-term")]
    [InlineData("ÄÖ Test 2", "test-2")]
    [InlineData("---", "")]
    public void Slugify_keeps_lowercase_letters_and_digits_separated_by_single_hyphens(string name, string expected) =>
        BrokerAccount.Slugify(name).ShouldBe(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("···")]
    public void A_name_without_letters_or_digits_is_rejected(string? name)
    {
        var result = Create(name: name);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(AccountErrors.InvalidCode);
        FieldsOf(result).ShouldContain(f => f.Field == "name");
    }

    [Fact]
    public void A_name_longer_than_the_limit_is_rejected() =>
        FieldsOf(Create(name: new string('a', BrokerAccount.MaxNameLength + 1))).ShouldContain(f => f.Field == "name");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("has space")]
    [InlineData("../etc/passwd")]
    public void Invalid_client_ids_are_rejected_on_create(string? clientId) =>
        FieldsOf(Create(clientId: clientId)).ShouldContain(f => f.Field == "clientId");

    [Theory]
    [InlineData("#1A2B3C", true)]
    [InlineData("#1a2b3c", true)]
    [InlineData("1A2B3C", false)]
    [InlineData("#12345", false)]
    [InlineData("#GGGGGG", false)]
    public void Colours_must_be_hex_triplets(string color, bool valid) =>
        FieldsOf(Create(color: color)).Any(f => f.Field == "color").ShouldBe(!valid);

    [Fact]
    public void The_colour_is_stored_in_upper_case() =>
        Create(color: "#1a2b3c").Value.Color.ShouldBe("#1A2B3C");

    [Fact]
    public void Notes_are_limited()
    {
        FieldsOf(Create(notes: new string('n', BrokerAccount.MaxNotesLength + 1))).ShouldContain(f => f.Field == "notes");
    }

    [Fact]
    public void All_problems_of_a_request_are_reported_together()
    {
        var result = Create(name: "", clientId: "x", color: "red", issuedOn: AccountData.Today.AddDays(1));

        FieldsOf(result).Select(f => f.Field).ShouldBe(["name", "clientId", "color", "apiKeyIssuedOn"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("1000000001", "1000••••01")]
    [InlineData("ABCD1234", "ABCD••34")]
    [InlineData("1234567", "1234•67")]
    [InlineData("123456", "••••••")]
    [InlineData("1234", "••••")]
    public void The_client_id_is_masked_to_the_first_four_and_last_two_characters(string clientId, string expected)
    {
        var account = AccountData.New(clientId: clientId);

        account.MaskedClientId.ShouldBe(expected);
    }
}
