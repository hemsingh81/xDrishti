using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;

namespace XDrishti.Application.Tests.Auth;

public sealed class LogoutHandlerTests
{
    private readonly AuthFixture _fx = new();

    [Fact]
    public async Task Logout_revokes_only_that_session_and_audits()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        await _fx.SignInAsync();
        var first = _fx.Data.Sessions[0];

        var result = await _fx.Logout().HandleAsync(new LogoutCommand(first.Id, AuthFixture.Client), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        first.RevokedAt.ShouldNotBeNull();
        _fx.Data.Sessions[1].RevokedAt.ShouldBeNull();
        _fx.Data.Events.Last().Event.ShouldBe(AuditEventType.Logout);
    }

    [Fact]
    public async Task Logging_out_twice_succeeds_and_audits_once()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        var command = new LogoutCommand(_fx.Data.Sessions.Single().Id, AuthFixture.Client);

        await _fx.Logout().HandleAsync(command, CancellationToken.None);
        var second = await _fx.Logout().HandleAsync(command, CancellationToken.None);

        second.IsSuccess.ShouldBeTrue();
        _fx.Data.Events.Count(e => e.Event == AuditEventType.Logout).ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_session_is_not_an_error()
    {
        var result = await _fx.Logout().HandleAsync(new LogoutCommand(Guid.NewGuid(), AuthFixture.Client), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }
}
