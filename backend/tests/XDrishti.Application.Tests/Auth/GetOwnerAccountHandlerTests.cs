using XDrishti.Application.Auth;

namespace XDrishti.Application.Tests.Auth;

public sealed class GetOwnerAccountHandlerTests
{
    private readonly AuthFixture _fx = new();

    private ValueTask<XDrishti.Domain.Common.Result<AccountResponse>> GetAsync(Guid sessionId) =>
        _fx.GetAccount().HandleAsync(new GetOwnerAccountQuery(sessionId), CancellationToken.None);

    [Fact]
    public async Task Returns_the_owner_of_an_active_session()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        _fx.Clock.Advance(TimeSpan.FromMinutes(3));
        _fx.Data.Owners.Single().ChangePassword("h:other", _fx.Clock.GetUtcNow());

        var result = await GetAsync(_fx.Data.Sessions.Single().Id);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(new AccountResponse(AuthFixture.OwnerEmail, "The Owner", AuthFixture.Start, AuthFixture.Start.AddMinutes(3)));
    }

    [Fact]
    public async Task A_revoked_session_is_unauthenticated()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        var session = _fx.Data.Sessions.Single();
        session.Revoke(_fx.Clock.GetUtcNow());

        (await GetAsync(session.Id)).Error.ShouldBe(AuthErrors.Unauthenticated);
    }

    [Fact]
    public async Task An_unknown_session_is_unauthenticated()
    {
        _fx.SeedOwner();

        (await GetAsync(Guid.NewGuid())).Error.ShouldBe(AuthErrors.Unauthenticated);
    }
}
