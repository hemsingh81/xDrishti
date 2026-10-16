using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;

namespace XDrishti.Application.Tests.Auth;

public sealed class OwnerEmailLoggingTests
{
    private const string Hostile = "owner@example.invalid\r\n2026-10-10 INFO Login succeeded for admin";

    [Fact]
    public async Task The_owner_email_is_sanitised_before_it_reaches_the_log()
    {
        var fx = new AuthFixture();
        fx.Data.Owners.Add(OwnerAccount.Create(Hostile, "The Owner", "h:" + AuthFixture.OwnerPassword, fx.Clock.GetUtcNow()));
        var session = AuthSession.Start(fx.Data.Owners.Single().Id, "token-hash", fx.Clock.GetUtcNow(), AuthFixture.Client.Ip, AuthFixture.Client.UserAgent);
        fx.Data.Sessions.Add(session);
        var logger = new CapturingLogger<LogoutHandler>();
        var handler = new LogoutHandler(fx.Data, fx.Data, fx.Data, fx.Clock, logger);

        var result = await handler.HandleAsync(new LogoutCommand(session.Id, AuthFixture.Client), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var line = logger.Messages.ShouldHaveSingleItem();
        line.ShouldNotContain("\r");
        line.ShouldNotContain("\n");
    }
}
