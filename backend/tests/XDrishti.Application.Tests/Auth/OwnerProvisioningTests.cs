using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Auth;

public sealed class OwnerProvisioningTests
{
    private readonly AuthFixture _fx = new();

    private ValueTask<Result<ProvisionOutcome>> ProvisionAsync(string? email = AuthFixture.OwnerEmail, string? name = "The Owner", string? password = AuthFixture.OwnerPassword) =>
        _fx.Provision().HandleAsync(new ProvisionOwnerCommand(email, name, password), CancellationToken.None);

    [Fact]
    public async Task Creates_the_owner_with_a_hashed_password_and_audits_it()
    {
        var result = await ProvisionAsync();

        result.Value.ShouldBe(ProvisionOutcome.Created);
        var owner = _fx.Data.Owners.ShouldHaveSingleItem();
        owner.Email.ShouldBe(AuthFixture.OwnerEmail);
        owner.PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
        owner.PasswordHash.ShouldNotBe(AuthFixture.OwnerPassword);
        _fx.Data.Events.ShouldHaveSingleItem().Event.ShouldBe(AuditEventType.OwnerProvisioned);
    }

    [Fact]
    public async Task Running_it_again_changes_nothing()
    {
        await ProvisionAsync();
        var hash = _fx.Data.Owners.Single().PasswordHash;

        var again = await ProvisionAsync(password: "a different password");

        again.Value.ShouldBe(ProvisionOutcome.AlreadyExists);
        _fx.Data.Owners.ShouldHaveSingleItem().PasswordHash.ShouldBe(hash);
        _fx.Data.Events.Count.ShouldBe(1);
    }

    [Fact]
    public async Task An_existing_owner_needs_no_secrets()
    {
        _fx.SeedOwner();

        var result = await ProvisionAsync(null, null, null);

        result.Value.ShouldBe(ProvisionOutcome.AlreadyExists);
    }

    [Theory]
    [InlineData(null, "name", "pw", "auth.owner_email_invalid")]
    [InlineData("not-an-email", "name", "pw", "auth.owner_email_invalid")]
    [InlineData("a@b.c", " ", "pw", "auth.owner_name_invalid")]
    [InlineData("a@b.c", "name", "", "auth.owner_password_invalid")]
    public async Task Missing_secrets_fail_when_no_owner_exists(string? email, string? name, string? password, string code)
    {
        var result = await ProvisionAsync(email, name, password);

        result.Error.Code.ShouldBe(code);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        _fx.Data.Owners.ShouldBeEmpty();
    }
}
