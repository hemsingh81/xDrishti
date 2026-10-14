using XDrishti.Application.Auth;

namespace XDrishti.Application.Tests.Auth;

/// <summary>The hub closes a connection with its session: which sessions have ended, without sliding the idle window.</summary>
public sealed class ListEndedSessionsHandlerTests
{
    private readonly AuthFixture _fx = new();

    private ListEndedSessionsHandler Handler() => new(_fx.Data, _fx.Clock);

    private async Task<IReadOnlyList<Guid>> EndedAsync(params Guid[] ids) =>
        await Handler().HandleAsync(new ListEndedSessionsQuery(ids), CancellationToken.None);

    [Fact]
    public async Task A_session_in_use_has_not_ended()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();

        (await EndedAsync(_fx.Data.Sessions.Single().Id)).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_logged_out_session_has_ended()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        var id = _fx.Data.Sessions.Single().Id;
        _fx.Data.Sessions.Single().Revoke(_fx.Clock.GetUtcNow());

        (await EndedAsync(id)).ShouldBe([id]);
    }

    [Fact]
    public async Task A_session_idle_for_thirty_minutes_has_ended_and_is_not_touched_by_the_look()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        var id = _fx.Data.Sessions.Single().Id;

        _fx.Clock.Advance(TimeSpan.FromMinutes(30));

        (await EndedAsync(id)).ShouldBe([id]);
        _fx.Data.Sessions.Single().RevokedAt.ShouldBeNull(); // only reads; the next request ends and audits it
    }

    [Fact]
    public async Task Looking_does_not_keep_an_idle_session_alive()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        var id = _fx.Data.Sessions.Single().Id;

        for (var i = 0; i < 5; i++)
        {
            _fx.Clock.Advance(TimeSpan.FromMinutes(5));
            (await EndedAsync(id)).ShouldBeEmpty();
        }

        _fx.Clock.Advance(TimeSpan.FromMinutes(5));

        (await EndedAsync(id)).ShouldBe([id]); // thirty minutes since the sign-in: every look in between left the idle window alone
    }

    [Fact]
    public async Task A_session_older_than_twelve_hours_has_ended()
    {
        _fx.SeedOwner();
        await _fx.SignInAsync();
        var id = _fx.Data.Sessions.Single().Id;

        _fx.Clock.Advance(TimeSpan.FromHours(12));

        (await EndedAsync(id)).ShouldBe([id]);
    }

    [Fact]
    public async Task An_unknown_session_counts_as_ended()
    {
        var unknown = Guid.NewGuid();

        (await EndedAsync(unknown)).ShouldBe([unknown]);
    }
}
