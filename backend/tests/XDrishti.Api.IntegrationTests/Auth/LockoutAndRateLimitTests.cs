using System.Net;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>AC 5: lockout after five failures, and the per-address rate limit on the login route.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LockoutAndRateLimitTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_and_even_the_right_password_is_refused()
    {
        await factory.RestoreOwnerAsync();
        try
        {
            var client = factory.CreateAnonymousClient();
            for (var i = 0; i < 4; i++)
            {
                (await client.LoginAsync(ApiFactory.OwnerEmail, "wrong", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            var locking = await client.LoginAsync(ApiFactory.OwnerEmail, "wrong", Ct);
            var duringLock = await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);

            locking.StatusCode.ShouldBe((HttpStatusCode)423);
            var (code, retry) = await locking.ReadProblemAsync(Ct);
            code.ShouldBe("auth.locked");
            retry.ShouldNotBeNull().ShouldBeInRange(890, 900);
            locking.Headers.RetryAfter?.Delta.ShouldNotBeNull();

            duringLock.StatusCode.ShouldBe((HttpStatusCode)423);
            (await duringLock.ReadProblemAsync(Ct)).Code.ShouldBe("auth.locked");
            ApiFactory.SessionCookieValue(duringLock).ShouldBeNull();
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task The_lock_ends_after_fifteen_minutes()
    {
        await factory.RestoreOwnerAsync();
        try
        {
            var client = factory.CreateAnonymousClient();
            for (var i = 0; i < 5; i++)
            {
                await client.LoginAsync(ApiFactory.OwnerEmail, "wrong", Ct);
            }

            factory.Clock.Advance(TimeSpan.FromMinutes(15).Add(TimeSpan.FromSeconds(1)));

            (await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        finally
        {
            factory.Clock.Reset();
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task A_success_resets_the_failure_counter()
    {
        await factory.RestoreOwnerAsync();
        var client = factory.CreateAnonymousClient();
        for (var i = 0; i < 4; i++)
        {
            await client.LoginAsync(ApiFactory.OwnerEmail, "wrong", Ct);
        }

        (await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        for (var i = 0; i < 4; i++)
        {
            (await client.LoginAsync(ApiFactory.OwnerEmail, "wrong", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task More_than_ten_login_attempts_a_minute_from_one_address_get_429()
    {
        var client = factory.CreateAnonymousClient();
        for (var i = 0; i < 10; i++)
        {
            (await client.LoginAsync("nobody@example.invalid", "wrong", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var limited = await client.LoginAsync("nobody@example.invalid", "wrong", Ct);

        limited.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await limited.ReadProblemAsync(Ct)).Code.ShouldBe("auth.rate_limited");
        limited.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_rate_limit_is_per_client_address()
    {
        var busy = factory.CreateAnonymousClient();
        for (var i = 0; i < 11; i++)
        {
            await busy.LoginAsync("nobody@example.invalid", "wrong", Ct);
        }

        var other = factory.CreateAnonymousClient();

        (await other.LoginAsync("nobody@example.invalid", "wrong", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
