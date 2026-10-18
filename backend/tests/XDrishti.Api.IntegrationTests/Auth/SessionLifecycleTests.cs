using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>AC 3 (logout, idle and absolute expiry) and AC 4 (CSRF).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class SessionLifecycleTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Logout_without_the_csrf_header_is_403_and_keeps_the_session()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var noHeader = factory.CreateAnonymousClient();
        noHeader.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        var response = await noHeader.PostAsync("/api/auth/logout", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.csrf_invalid");
        (await signedIn.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Logout_with_a_wrong_csrf_token_is_403()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var wrong = factory.CreateAnonymousClient();
        wrong.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);
        wrong.DefaultRequestHeaders.Add("X-CSRF-Token", signedIn.Session.CsrfToken + "x");

        (await wrong.PostAsync("/api/auth/logout", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_clears_the_cookie()
    {
        var signedIn = await factory.SignInAsync(Ct);

        var response = await signedIn.Client.PostAsync("/api/auth/logout", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cleared = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("xd.sid=", StringComparison.Ordinal)).ToLowerInvariant();
        cleared.ShouldContain("expires=thu, 01 jan 1970");
        (await signedIn.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await signedIn.Client.GetAsync("/api/system/status", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_ends_only_that_session()
    {
        var one = await factory.SignInAsync(Ct);
        var two = await factory.SignInAsync(Ct);

        await one.Client.PostAsync("/api/auth/logout", Ct);

        (await two.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Login_does_not_need_a_csrf_token_even_with_a_cookie_present()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        var response = await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Safe_methods_need_no_csrf_token()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var cookieOnly = factory.CreateAnonymousClient();
        cookieOnly.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        (await cookieOnly.GetAsync("/api/system/status", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Thirty_minutes_without_a_request_ends_the_session_and_is_audited()
    {
        var signedIn = await factory.SignInAsync(Ct);
        try
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(31));

            var response = await signedIn.Client.GetAsync("/api/system/status", Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.session_expired");
            response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("xd.sid=", StringComparison.Ordinal)).ToLowerInvariant().ShouldContain("expires=thu, 01 jan 1970");
            (await signedIn.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().AnyAsync(e => e.Event == Domain.Auth.AuditEventType.SessionExpiredIdle, Ct))).ShouldBeTrue();
        }
        finally
        {
            await factory.ResetClockAsync();
        }
    }

    [Fact]
    public async Task Regular_activity_keeps_the_session_until_the_absolute_lifetime()
    {
        var signedIn = await factory.SignInAsync(Ct);
        try
        {
            for (var i = 0; i < 35; i++) // a request every 20 minutes up to 11 h 40
            {
                factory.Clock.Advance(TimeSpan.FromMinutes(20));
                (await signedIn.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            }

            factory.Clock.Advance(TimeSpan.FromMinutes(20)); // 12 h after sign-in

            var response = await signedIn.Client.GetAsync("/api/auth/verify", Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.session_expired");
            (await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().AnyAsync(e => e.Event == Domain.Auth.AuditEventType.SessionExpiredAbsolute, Ct))).ShouldBeTrue();
        }
        finally
        {
            await factory.ResetClockAsync();
        }
    }

    [Fact]
    public async Task Last_seen_is_written_at_most_once_a_minute()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var tokenHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(signedIn.CookieValue)));
        try
        {
            var created = await factory.WithDbAsync(db => db.AuthSessions.AsNoTracking().Where(s => s.TokenHash == tokenHash).Select(s => s.LastSeenAt).SingleAsync(Ct));

            factory.Clock.Advance(TimeSpan.FromSeconds(20));
            await signedIn.Client.GetAsync("/api/auth/verify", Ct);
            var afterQuick = await factory.WithDbAsync(db => db.AuthSessions.AsNoTracking().Where(s => s.TokenHash == tokenHash).Select(s => s.LastSeenAt).SingleAsync(Ct));

            factory.Clock.Advance(TimeSpan.FromSeconds(50));
            await signedIn.Client.GetAsync("/api/auth/verify", Ct);
            var afterMinute = await factory.WithDbAsync(db => db.AuthSessions.AsNoTracking().Where(s => s.TokenHash == tokenHash).Select(s => s.LastSeenAt).SingleAsync(Ct));

            afterQuick.ShouldBe(created);
            afterMinute.ShouldBeGreaterThan(created);
        }
        finally
        {
            await factory.ResetClockAsync();
        }
    }
}
