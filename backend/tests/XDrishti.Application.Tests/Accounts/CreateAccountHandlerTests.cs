using System.Text.Json;
using XDrishti.Application.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Accounts;

public sealed class CreateAccountHandlerTests
{
    private readonly AccountsFixture _fx = new();

    private ValueTask<Result<AccountDetail>> CreateAsync(CreateAccountInput input) =>
        _fx.Create().HandleAsync(new CreateAccountCommand(input, AccountsFixture.Actor), CancellationToken.None);

    private static IEnumerable<string> Fields(Result result) => result.Error.Fields?.Select(f => f.Field) ?? [];

    [Fact]
    public async Task A_valid_account_is_created_with_every_role_off_and_audited_with_the_actor()
    {
        var result = await CreateAsync(Inputs.Account());

        result.IsSuccess.ShouldBeTrue();
        var detail = result.Value;
        detail.Slug.ShouldBe("example-one");
        detail.Broker.ShouldBe("dhan");
        detail.TokenStatus.ShouldBe("not_connected");
        detail.Connection.Status.ShouldBe("not_connected");
        detail.Roles.Data.ShouldBe(new DataRoleView(false, false, false));
        _fx.Data.Accounts.ShouldHaveSingleItem();
        var entry = _fx.Data.Events.ShouldHaveSingleItem();
        entry.Event.ShouldBe(AccountEventType.AccountCreated);
        entry.Actor.ShouldBe(AccountsFixture.Actor);
        entry.AccountId.ShouldBe(detail.Id);
        entry.At.ShouldBe(AccountsFixture.Start);
    }

    [Fact]
    public async Task The_response_and_the_audit_entry_never_contain_the_client_id()
    {
        var result = await CreateAsync(Inputs.Account());

        JsonSerializer.Serialize(result.Value).ShouldNotContain(AccountsFixture.ClientId);
        result.Value.MaskedClientId.ShouldBe("1000••••01");
        _fx.Data.Events.ShouldAllBe(e => e.Detail == null || !e.Detail.Contains(AccountsFixture.ClientId, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_wizard_can_create_an_account_with_primary_data_trading_and_portfolio_in_one_go()
    {
        var input = Inputs.Account(
            roles: Inputs.Roles(trading: true, portfolio: true, data: new DataRoleInput(true, "primary")),
            trading: Inputs.Trading(_fx.Standard.Id, "manual_live"),
            portfolio: Inputs.Portfolio("trading", new DateOnly(2024, 4, 1)),
            data: new DataInput(300));

        var result = await CreateAsync(input);

        var detail = result.Value;
        detail.Roles.Data.ShouldBe(new DataRoleView(true, true, false));
        detail.Roles.Trading.ShouldBe(new TradingRoleView(true, "manual_live", "Standard"));
        detail.Trading.Styles.ShouldBe(["intraday"]);
        detail.Trading.Instruments.Intraday.ShouldBe(["EQ_MIS", "FUT"]);
        detail.Portfolio.DefaultBucket.ShouldBe("Trading"); // stored with the bucket's own spelling
        detail.Portfolio.HistoryFrom.ShouldBe(new DateOnly(2024, 4, 1));
        detail.Data.LiveFeedBudget.ShouldBe(300);
        _fx.Data.Events.ShouldHaveSingleItem().Detail!.ShouldContain("data primary, trading on, portfolio on");
    }

    [Fact]
    public async Task Nothing_is_stored_when_any_part_is_invalid()
    {
        var input = Inputs.Account(roles: Inputs.Roles(trading: true, portfolio: false), trading: Inputs.Trading(Guid.NewGuid()));

        var result = await CreateAsync(input);

        result.Error.Code.ShouldBe(AccountErrors.InvalidCode);
        Fields(result).ShouldContain("trading.riskProfileId");
        _fx.Data.Accounts.ShouldBeEmpty();
        _fx.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Errors_of_different_kinds_are_returned_together()
    {
        var input = Inputs.Account(name: "", clientId: "x", secrets: Inputs.Secrets(apiSecret: null));

        var result = await CreateAsync(input);

        result.Error.Code.ShouldBe(AccountErrors.InvalidCode);
        Fields(result).ShouldBe(["name", "clientId", "secrets.apiSecret"], ignoreOrder: true);
    }

    [Fact]
    public async Task Unparsable_trading_values_do_not_hide_portfolio_and_data_errors()
    {
        var input = Inputs.Account(
            trading: new TradingInput("auto", null, null, null, null, null, null),
            portfolio: Inputs.Portfolio("Nope"),
            data: new DataInput(-1));

        var result = await CreateAsync(input);

        Fields(result).ShouldBe(["trading.mode", "portfolio.defaultBucket", "data.liveFeedBudget"], ignoreOrder: true);
    }

    [Fact]
    public async Task Domain_validation_errors_carry_the_field_names()
    {
        var result = await CreateAsync(Inputs.Account(name: "", clientId: "x", apiKeyIssuedOn: new DateOnly(2030, 1, 1)));

        Fields(result).ShouldBe(["name", "clientId", "apiKeyIssuedOn"], ignoreOrder: true);
    }

    [Fact]
    public async Task Enabling_trading_without_complete_settings_names_the_missing_fields()
    {
        var result = await CreateAsync(Inputs.Account(roles: Inputs.Roles(trading: true, portfolio: false)));

        Fields(result).ShouldContain("roles.trading");
        Fields(result).ShouldContain("trading.riskProfileId");
        Fields(result).ShouldContain("trading.styles");
    }

    [Fact]
    public async Task Unknown_codes_are_field_errors_not_exceptions()
    {
        var trading = new TradingInput("auto", null, "wire", null, ["monthly"], new InstrumentsInput(["OPT"], null, null), null);

        var result = await CreateAsync(Inputs.Account(trading: trading, roles: new RolesInput(new DataRoleInput(true, "main"), null, null)));

        Fields(result).ShouldContain("roles.data.kind");
        Fields(result).ShouldContain("trading.mode");
        Fields(result).ShouldContain("trading.capitalSource");
        Fields(result).ShouldContain("trading.styles");
        Fields(result).ShouldContain("trading.instruments.intraday");
    }

    [Fact]
    public async Task An_unknown_bucket_is_rejected()
    {
        var result = await CreateAsync(Inputs.Account(portfolio: Inputs.Portfolio("Nope")));

        Fields(result).ShouldContain("portfolio.defaultBucket");
    }

    [Fact]
    public async Task A_similar_name_is_a_conflict()
    {
        await _fx.CreatedAsync();

        var result = await CreateAsync(Inputs.Account(name: "EXAMPLE  one!"));

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        _fx.Data.Accounts.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_API_key_and_secret_go_into_the_vault_and_only_their_status_comes_back()
    {
        var result = await CreateAsync(Inputs.Account(secrets: Inputs.Secrets($"  {AccountsFixture.ApiKey} ", AccountsFixture.ApiSecret)));

        var detail = result.Value;
        detail.Secrets.ShouldBe([new SecretStatusView("apiKey", true, AccountsFixture.Start), new SecretStatusView("apiSecret", true, AccountsFixture.Start)]);
        _fx.Data.Secrets[(detail.Id, BrokerSecretName.ApiKey)].Value.Reveal().ShouldBe(AccountsFixture.ApiKey);
        _fx.Data.Secrets[(detail.Id, BrokerSecretName.ApiSecret)].Value.Reveal().ShouldBe(AccountsFixture.ApiSecret);
        JsonSerializer.Serialize(detail).ShouldNotContain(AccountsFixture.Marker);
        var entry = _fx.Data.Events.ShouldHaveSingleItem().Detail!;
        entry.ShouldContain("API key and API secret set");
        entry.ShouldNotContain(AccountsFixture.Marker);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", " ")]
    [InlineData("with space", "line\nbreak")]
    public async Task Both_secrets_are_required_and_must_be_single_tokens(string? apiKey, string? apiSecret)
    {
        var result = await CreateAsync(Inputs.Account(secrets: Inputs.Secrets(apiKey, apiSecret)));

        Fields(result).ShouldBe(["secrets.apiKey", "secrets.apiSecret"], ignoreOrder: true);
        _fx.Data.Accounts.ShouldBeEmpty();
        _fx.Data.Secrets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Leaving_the_secrets_object_out_names_both_fields()
    {
        var result = await CreateAsync(Inputs.Account() with { Secrets = null });

        Fields(result).ShouldBe(["secrets.apiKey", "secrets.apiSecret"], ignoreOrder: true);
    }

    [Fact]
    public async Task A_Dhan_key_was_generated_today_in_India_unless_told_otherwise()
    {
        _fx.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)); // already 11 Oct 01:30 in India

        var byDefault = await CreateAsync(Inputs.Account());
        var given = await CreateAsync(Inputs.Account(name: "Example Two", apiKeyIssuedOn: new DateOnly(2026, 3, 1)));

        byDefault.Value.ApiKeyIssuedOn.ShouldBe(new DateOnly(2026, 10, 11));
        byDefault.Value.ApiKeyExpiresOn.ShouldBe(new DateOnly(2027, 10, 11));
        given.Value.ApiKeyExpiresOn.ShouldBe(new DateOnly(2027, 3, 1));
    }

    [Fact]
    public async Task A_Dhan_account_needs_a_client_id()
    {
        var result = await CreateAsync(Inputs.Account(clientId: null));

        Fields(result).ShouldBe(["clientId"]);
    }

    [Fact]
    public async Task A_created_account_is_a_Dhan_account_with_all_secrets_set()
    {
        var detail = (await CreateAsync(Inputs.Account())).Value;

        detail.Broker.ShouldBe("dhan");
        detail.Secrets.ShouldAllBe(s => s.Set);
    }

    [Fact]
    public async Task A_collision_at_save_time_stores_no_secret()
    {
        _fx.Data.CollidingSaves = 1;

        await CreateAsync(Inputs.Account());

        _fx.Data.Secrets.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_request_never_shows_its_secrets_in_its_text_form()
    {
        var input = Inputs.Account();

        input.ToString().ShouldNotContain(AccountsFixture.Marker);
        input.Secrets!.ToString().ShouldContain("[redacted]");
        new UpdateSecretsInput(AccountsFixture.ApiKey, AccountsFixture.ApiSecret, null).ToString().ShouldNotContain(AccountsFixture.Marker);
    }

    [Fact]
    public async Task A_second_primary_Data_account_is_a_conflict_and_nothing_is_stored()
    {
        await _fx.CreatedAsync(Inputs.Account(roles: Inputs.DataRole("primary")));

        var result = await CreateAsync(Inputs.Account(name: "Example Two", roles: Inputs.DataRole("primary")));

        result.Error.Code.ShouldBe(AccountErrors.ConflictCode);
        _fx.Data.Accounts.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_name_freed_by_archiving_can_be_used_again()
    {
        var first = await _fx.CreatedAsync();
        first.Archive(AccountsFixture.Start).IsSuccess.ShouldBeTrue();

        var result = await CreateAsync(Inputs.Account());

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_collision_at_save_time_is_reported_as_a_conflict()
    {
        _fx.Data.CollidingSaves = 1;

        var result = await CreateAsync(Inputs.Account());

        result.Error.ShouldBe(AccountErrors.ConcurrentChange);
        _fx.Data.Accounts.ShouldBeEmpty();
        _fx.Data.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_history_start_in_the_future_is_invalid_using_the_indian_calendar_date()
    {
        _fx.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 10, 20, 0, 0, TimeSpan.Zero)); // already 11 Oct 01:30 in India

        var today = await CreateAsync(Inputs.Account(portfolio: Inputs.Portfolio("Trading", new DateOnly(2026, 10, 11))));
        var tomorrow = await CreateAsync(Inputs.Account(name: "Example Two", portfolio: Inputs.Portfolio("Trading", new DateOnly(2026, 10, 12))));

        today.IsSuccess.ShouldBeTrue();
        Fields(tomorrow).ShouldContain("portfolio.historyFrom");
    }
}
