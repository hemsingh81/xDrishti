using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Auth;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>AC 2, 3 (cookie, session endpoint) and 7 (hash at rest).</summary>
[Collection(ApiTestSuite.Name)]
public sealed class LoginTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Valid_credentials_return_the_session_info_and_an_opaque_cookie()
    {
        var response = await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = (await response.Content.ReadFromJsonAsync<SessionInfo>(Ct))!;
        session.User.ShouldBe(new UserInfo(ApiFactory.OwnerEmail, ApiFactory.OwnerName));
        session.CsrfToken.ShouldNotBeNullOrWhiteSpace();
        session.IdleTimeoutSeconds.ShouldBe(1800);
        session.ExpiresAt.ShouldBeInRange(DateTimeOffset.UtcNow.AddHours(11), DateTimeOffset.UtcNow.AddHours(13));
        ApiFactory.SessionCookieValue(response).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Email_is_matched_ignoring_case_and_surrounding_spaces()
    {
        var response = await factory.CreateAnonymousClient().LoginAsync("  " + ApiFactory.OwnerEmail.ToUpperInvariant() + " ", factory.OwnerPassword, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_cookie_is_http_only_same_site_lax_site_wide_and_not_secure_over_plain_http()
    {
        var response = await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("xd.sid=", StringComparison.Ordinal)).ToLowerInvariant();
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("samesite=lax"); // Lax: the brokers' top-level redirects to the callbacks carry the session (ADR 0008)
        cookie.ShouldContain("path=/");
        cookie.ShouldContain("expires=");
        cookie.ShouldNotContain("secure");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
    }

    [Fact]
    public async Task The_cookie_is_secure_when_the_proxy_says_the_request_was_https()
    {
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");

        var response = await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);

        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("xd.sid=", StringComparison.Ordinal))
            .ToLowerInvariant().ShouldContain("secure");
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_get_the_same_401()
    {
        var client = factory.CreateAnonymousClient();

        var wrongPassword = await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword + "x", Ct);
        var unknownEmail = await client.LoginAsync("nobody@example.invalid", factory.OwnerPassword, Ct);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await wrongPassword.ReadProblemAsync(Ct)).Code.ShouldBe("auth.invalid_credentials");
        (await unknownEmail.ReadProblemAsync(Ct)).Code.ShouldBe("auth.invalid_credentials");
        ApiFactory.SessionCookieValue(wrongPassword).ShouldBeNull();
        ApiFactory.SessionCookieValue(unknownEmail).ShouldBeNull();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("owner@example.invalid", "")]
    public async Task Empty_credentials_get_the_same_401(string? email, string? password)
    {
        var response = await factory.CreateAnonymousClient().LoginAsync(email, password, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.invalid_credentials");
    }

    [Fact]
    public async Task The_session_endpoint_returns_the_same_session_for_the_cookie()
    {
        var signedIn = await factory.SignInAsync(Ct);

        var response = await signedIn.Client.GetAsync("/api/auth/session", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var session = (await response.Content.ReadFromJsonAsync<SessionInfo>(Ct))!;
        session.User.ShouldBe(signedIn.Session.User);
        session.CsrfToken.ShouldBe(signedIn.Session.CsrfToken); // survives a page reload
        session.ExpiresAt.ShouldBe(signedIn.Session.ExpiresAt);
    }

    [Fact]
    public async Task A_new_login_leaves_other_sessions_valid()
    {
        var first = await factory.SignInAsync(Ct);
        await factory.SignInAsync(Ct);

        (await first.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Only_hashes_are_stored_for_sessions_and_the_password()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var expectedHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(signedIn.CookieValue)));

        var (sessionRow, ownerHash) = await factory.WithDbAsync(async db => (
            await db.AuthSessions.AsNoTracking().SingleAsync(s => s.TokenHash == expectedHash, Ct),
            (await db.OwnerAccounts.AsNoTracking().SingleAsync(Ct)).PasswordHash));

        sessionRow.TokenHash.ShouldNotBe(signedIn.CookieValue);
        ownerHash.ShouldStartWith("$pbkdf2-sha512$");
        ownerHash.ShouldNotContain(factory.OwnerPassword);
    }

    [Fact]
    public async Task The_owner_was_provisioned_from_the_secrets()
    {
        var owner = await factory.WithDbAsync(db => db.OwnerAccounts.AsNoTracking().SingleAsync(Ct));

        owner.Email.ShouldBe(ApiFactory.OwnerEmail);
        owner.DisplayName.ShouldBe(ApiFactory.OwnerName);
    }

    [Fact]
    public async Task The_client_address_comes_from_the_trusted_proxy_header()
    {
        var client = factory.CreateAnonymousClient();
        client.DefaultRequestHeaders.Remove("X-Forwarded-For");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.77");

        await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);

        var latest = await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().OrderByDescending(e => e.At).FirstAsync(Ct));
        latest.Ip.ShouldBe("198.51.100.77");
    }
}
