using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Domain.Tests.Accounts;

/// <summary>Dhan API keys are valid for 12 months from the day they were generated (ADR 0008).</summary>
public sealed class BrokerApiKeyExpiryTests
{
    private static Result<BrokerAccount> Create(DateOnly? issuedOn, string? clientId = "1000000001") =>
        BrokerAccount.Create("Example One", clientId, issuedOn, null, null, AccountData.Today, AccountData.T0);

    private static IReadOnlyList<FieldError> FieldsOf(Result result) => result.Error.Fields ?? [];

    [Fact]
    public void A_Dhan_key_issued_today_by_default_expires_twelve_months_later()
    {
        var account = Create(null).Value;

        account.ApiKeyIssuedOn.ShouldBe(AccountData.Today);
        account.ApiKeyExpiresOn.ShouldBe(new DateOnly(2027, 10, 10));
    }

    [Fact]
    public void A_given_issue_date_sets_the_expiry()
    {
        var account = Create(new DateOnly(2026, 2, 28)).Value;

        account.ApiKeyExpiresOn.ShouldBe(new DateOnly(2027, 2, 28));
    }

    [Fact]
    public void An_issue_date_in_the_future_is_refused() =>
        FieldsOf(Create(AccountData.Today.AddDays(1))).ShouldContain(f => f.Field == "apiKeyIssuedOn");

    [Theory]
    [InlineData(2025, 10, 10, false)]
    [InlineData(2025, 10, 11, true)]
    public void A_key_that_has_expired_already_is_refused(int year, int month, int day, bool valid) =>
        Create(new DateOnly(year, month, day)).IsSuccess.ShouldBe(valid);

    [Fact]
    public void Changing_the_issue_date_moves_the_expiry()
    {
        var account = Create(null).Value;
        var later = AccountData.T0.AddDays(30);

        var result = account.ChangeApiKeyIssuedOn(AccountData.Today.AddDays(30), AccountData.Today.AddDays(30), later);

        result.IsSuccess.ShouldBeTrue();
        account.ApiKeyExpiresOn.ShouldBe(new DateOnly(2027, 11, 9));
        account.UpdatedAt.ShouldBe(later);
    }

    [Fact]
    public void A_refused_issue_date_changes_nothing()
    {
        var account = Create(null).Value;

        var result = account.ChangeApiKeyIssuedOn(AccountData.Today.AddDays(2), AccountData.Today, AccountData.T0.AddHours(1));

        result.IsFailure.ShouldBeTrue();
        account.ApiKeyIssuedOn.ShouldBe(AccountData.Today);
        account.UpdatedAt.ShouldBe(AccountData.T0);
    }

    [Fact]
    public void A_Dhan_account_needs_a_client_id() =>
        FieldsOf(Create(null, clientId: null)).ShouldContain(f => f.Field == "clientId");
}
