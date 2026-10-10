using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Tests.Auth;

public sealed class ResetOwnerPasswordHandlerTests
{
    private readonly AuthFixture _fx = new();

    private ValueTask<Result> ResetAsync(string? password = "the new password") =>
        _fx.Reset().HandleAsync(new ResetOwnerPasswordCommand(AuthFixture.OwnerEmail, "The Owner", password), CancellationToken.None);

    [Fact]
    public async Task Replaces_the_hash_and_revokes_every_session()
    {
        var owner = _fx.SeedOwner();
        await _fx.SignInAsync();
        await _fx.SignInAsync();

        var result = await ResetAsync();

        result.IsSuccess.ShouldBeTrue();
        owner.PasswordHash.ShouldBe("h:the new password");
        _fx.Data.Sessions.ShouldAllBe(s => s.RevokedAt != null);
        _fx.Data.Events.Last().Event.ShouldBe(AuditEventType.OwnerPasswordReset);
    }

    [Fact]
    public async Task The_old_password_stops_working_and_the_new_one_works()
    {
        _fx.SeedOwner();
        await ResetAsync();

        var old = await _fx.Login().HandleAsync(new LoginCommand(AuthFixture.OwnerEmail, AuthFixture.OwnerPassword, AuthFixture.Client), CancellationToken.None);
        var fresh = await _fx.Login().HandleAsync(new LoginCommand(AuthFixture.OwnerEmail, "the new password", AuthFixture.Client), CancellationToken.None);

        old.Error.ShouldBe(AuthErrors.InvalidCredentials);
        fresh.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Clears_a_lockout()
    {
        var owner = _fx.SeedOwner();
        for (var i = 0; i < 5; i++)
        {
            owner.RegisterFailure(_fx.Clock.GetUtcNow());
        }

        await ResetAsync();

        owner.IsLocked(_fx.Clock.GetUtcNow()).ShouldBeFalse();
    }

    [Fact]
    public async Task Fails_clearly_when_there_is_no_owner_yet()
    {
        var result = await ResetAsync();

        result.Error.ShouldBe(AuthErrors.OwnerMissing);
    }

    [Fact]
    public async Task Rejects_an_empty_password_and_changes_nothing()
    {
        var owner = _fx.SeedOwner();

        var result = await ResetAsync("");

        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        owner.PasswordHash.ShouldBe("h:" + AuthFixture.OwnerPassword);
    }
}
