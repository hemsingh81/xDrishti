using System.Text.Json;
using XDrishti.Application.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Accounts;

public sealed class UpdateSecretsHandlerTests
{
    private const string NewKey = "new-key-" + AccountsFixture.Marker;

    private const string NewSecret = "new-secret-" + AccountsFixture.Marker;

    private readonly AccountsFixture _fx = new();

    private static IEnumerable<string> Fields(Result result) => result.Error.Fields?.Select(f => f.Field) ?? [];

    private ValueTask<Result<AccountDetail>> UpdateAsync(Guid id, UpdateSecretsInput input) =>
        _fx.UpdateSecrets().HandleAsync(new UpdateSecretsCommand(id, input, AccountsFixture.Actor), CancellationToken.None);

    private string Stored(BrokerAccount account, BrokerSecretName name) => _fx.Data.Secrets[(account.Id, name)].Value.Reveal();

    [Fact]
    public async Task A_new_API_key_replaces_the_old_one_resets_the_Dhan_key_date_and_is_audited_without_the_value()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(apiKeyIssuedOn: new DateOnly(2026, 1, 15)));
        _fx.Clock.Advance(TimeSpan.FromDays(3));

        var result = await UpdateAsync(account.Id, new UpdateSecretsInput(NewKey, null, null));

        result.IsSuccess.ShouldBeTrue();
        Stored(account, BrokerSecretName.ApiKey).ShouldBe(NewKey);
        Stored(account, BrokerSecretName.ApiSecret).ShouldBe(AccountsFixture.ApiSecret);
        result.Value.ApiKeyIssuedOn.ShouldBe(new DateOnly(2026, 10, 13));
        result.Value.Secrets[0].UpdatedAt.ShouldBe(_fx.Clock.GetUtcNow());
        result.Value.Secrets[1].UpdatedAt.ShouldBe(AccountsFixture.Start);
        account.UpdatedAt.ShouldBe(_fx.Clock.GetUtcNow());
        var entry = _fx.Data.Events.Single(e => e.Event == AccountEventType.SecretsReplaced);
        entry.Detail.ShouldBe("API key replaced; API key issued on 2026-10-13");
        entry.Actor.ShouldBe(AccountsFixture.Actor);
        JsonSerializer.Serialize(result.Value).ShouldNotContain(AccountsFixture.Marker);
    }

    [Fact]
    public async Task Both_secrets_and_a_given_key_date_can_be_replaced_together()
    {
        var account = await _fx.CreatedAsync();

        var result = await UpdateAsync(account.Id, new UpdateSecretsInput(NewKey, NewSecret, new DateOnly(2026, 9, 1)));

        Stored(account, BrokerSecretName.ApiKey).ShouldBe(NewKey);
        Stored(account, BrokerSecretName.ApiSecret).ShouldBe(NewSecret);
        result.Value.ApiKeyExpiresOn.ShouldBe(new DateOnly(2027, 9, 1));
        _fx.Data.Events.Last().Detail.ShouldBe("API key and API secret replaced; API key issued on 2026-09-01");
    }

    [Fact]
    public async Task Only_the_secret_leaves_the_key_date_alone()
    {
        var account = await _fx.CreatedAsync(Inputs.Account(apiKeyIssuedOn: new DateOnly(2026, 1, 15)));

        var result = await UpdateAsync(account.Id, new UpdateSecretsInput(null, NewSecret, null));

        result.Value.ApiKeyIssuedOn.ShouldBe(new DateOnly(2026, 1, 15));
        _fx.Data.Events.Last().Detail.ShouldBe("API secret replaced");
    }

    [Fact]
    public async Task The_key_date_can_be_corrected_on_its_own()
    {
        var account = await _fx.CreatedAsync();

        var result = await UpdateAsync(account.Id, new UpdateSecretsInput(null, null, new DateOnly(2026, 6, 30)));

        result.Value.ApiKeyExpiresOn.ShouldBe(new DateOnly(2027, 6, 30));
        Stored(account, BrokerSecretName.ApiKey).ShouldBe(AccountsFixture.ApiKey);
    }

    [Theory]
    [InlineData("", null, "apiKey")]
    [InlineData(null, "  ", "apiSecret")]
    [InlineData("two words", null, "apiKey")]
    public async Task Empty_or_malformed_values_are_refused_and_nothing_changes(string? apiKey, string? apiSecret, string field)
    {
        var account = await _fx.CreatedAsync();

        var result = await UpdateAsync(account.Id, new UpdateSecretsInput(apiKey, apiSecret, null));

        Fields(result).ShouldBe([field]);
        Stored(account, BrokerSecretName.ApiKey).ShouldBe(AccountsFixture.ApiKey);
        Stored(account, BrokerSecretName.ApiSecret).ShouldBe(AccountsFixture.ApiSecret);
        _fx.Data.Events.ShouldNotContain(e => e.Event == AccountEventType.SecretsReplaced);
    }

    [Fact]
    public async Task An_empty_request_is_refused()
    {
        var account = await _fx.CreatedAsync();

        var result = await UpdateAsync(account.Id, new UpdateSecretsInput(null, null, null));

        result.Error.Code.ShouldBe(AccountErrors.InvalidCode);
    }

    [Fact]
    public async Task A_key_date_in_the_future_is_refused_and_the_new_key_is_not_stored()
    {
        var account = await _fx.CreatedAsync();

        var result = await UpdateAsync(account.Id, new UpdateSecretsInput(NewKey, null, new DateOnly(2026, 12, 1)));

        Fields(result).ShouldBe(["apiKeyIssuedOn"]);
        Stored(account, BrokerSecretName.ApiKey).ShouldBe(AccountsFixture.ApiKey);
    }

    [Fact]
    public async Task An_unknown_account_is_not_found()
    {
        var result = await UpdateAsync(Guid.NewGuid(), new UpdateSecretsInput(NewKey, null, null));

        result.Error.Code.ShouldBe(AccountErrors.NotFoundCode);
    }

    [Fact]
    public async Task A_collision_at_save_time_keeps_the_old_secrets()
    {
        var account = await _fx.CreatedAsync();
        _fx.Data.CollidingSaves = 1;

        var result = await UpdateAsync(account.Id, new UpdateSecretsInput(NewKey, NewSecret, null));

        result.Error.ShouldBe(AccountErrors.ConcurrentChange);
        Stored(account, BrokerSecretName.ApiKey).ShouldBe(AccountsFixture.ApiKey);
        Stored(account, BrokerSecretName.ApiSecret).ShouldBe(AccountsFixture.ApiSecret);
    }
}
