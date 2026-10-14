using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>P2-11: the owner's account page API - read the account, change the password, change name and e-mail.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AccountTests(ApiFactory factory)
{
    private const string NewPassword = "a brand new password 123";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> ChangePasswordAsync(HttpClient client, string? current, string? next) =>
        client.PostJsonAsync("/api/auth/change-password", new { currentPassword = current, newPassword = next }, Ct);

    private static Task<HttpResponseMessage> UpdateProfileAsync(HttpClient client, string? name, string? email) =>
        client.PutJsonAsync("/api/auth/profile", new { displayName = name, email }, Ct);

    private async Task<HttpClient> LoginClientAsync(string email, string password)
    {
        var client = factory.CreateAnonymousClient();
        using var response = await client.LoginAsync(email, password, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return client;
    }

    [Fact]
    public async Task The_account_shows_the_owner_and_never_secrets()
    {
        await factory.RestoreOwnerAsync();
        var signedIn = await factory.SignInAsync(Ct);

        var response = await signedIn.Client.GetAsync("/api/auth/account", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var body = await response.Content.ReadAsStringAsync(Ct);
        using var document = JsonDocument.Parse(body);
        document.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["email", "displayName", "createdAt", "passwordChangedAt"], ignoreOrder: true);
        var account = JsonSerializer.Deserialize<AccountResponse>(body, JsonSerializerOptions.Web)!;
        account.Email.ShouldBe(ApiFactory.OwnerEmail);
        account.DisplayName.ShouldNotBeNullOrWhiteSpace();
        account.PasswordChangedAt.ShouldBeGreaterThanOrEqualTo(account.CreatedAt);
        body.ShouldNotContain(factory.OwnerPassword);
    }

    [Fact]
    public async Task The_account_needs_a_session()
    {
        var response = await factory.CreateAnonymousClient().GetAsync("/api/auth/account", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_needs_a_session_and_a_csrf_token()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var noCsrf = factory.CreateAnonymousClient();
        noCsrf.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        var anonymous = await ChangePasswordAsync(factory.CreateAnonymousClient(), factory.OwnerPassword, NewPassword);
        var refused = await ChangePasswordAsync(noCsrf, factory.OwnerPassword, NewPassword);

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ReadProblemAsync(Ct)).Code.ShouldBe("auth.csrf_invalid");
    }

    [Fact]
    public async Task Changing_the_password_signs_out_other_sessions_keeps_this_one_and_switches_the_credentials()
    {
        await factory.RestoreOwnerAsync();
        try
        {
            var current = await factory.SignInAsync(Ct);
            var other = await factory.SignInAsync(Ct);
            var before = await (await current.Client.GetAsync("/api/auth/account", Ct)).ReadAsync<AccountResponse>(Ct);
            var since = factory.Clock.GetUtcNow();

            using var response = await ChangePasswordAsync(current.Client, factory.OwnerPassword, NewPassword);

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await current.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await other.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            using var oldLogin = await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
            oldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await LoginClientAsync(ApiFactory.OwnerEmail, NewPassword)).ShouldNotBeNull();
            var after = await (await current.Client.GetAsync("/api/auth/account", Ct)).ReadAsync<AccountResponse>(Ct);
            after.PasswordChangedAt.ShouldBeGreaterThanOrEqualTo(before.PasswordChangedAt);
            var audited = await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().Where(e => e.Event == AuditEventType.PasswordChanged && e.At >= since).ToListAsync(Ct));
            audited.Count.ShouldBe(1);
            audited[0].Email.ShouldBe(ApiFactory.OwnerEmail);
            audited[0].Detail.ShouldNotBeNull().ShouldNotContain(NewPassword);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task Neither_password_reaches_the_logs_or_the_audit_list()
    {
        await factory.RestoreOwnerAsync();
        try
        {
            var signedIn = await factory.SignInAsync(Ct);
            await ChangePasswordAsync(signedIn.Client, "wrong-" + NewPassword, NewPassword);
            await ChangePasswordAsync(signedIn.Client, factory.OwnerPassword, NewPassword);

            var audit = await (await signedIn.Client.GetAsync("/api/auth/audit?take=50", Ct)).Content.ReadAsStringAsync(Ct);

            audit.ShouldContain("PasswordChanged");
            audit.ShouldNotContain(NewPassword);
            audit.ShouldNotContain(factory.OwnerPassword);
            factory.Logs.All.ShouldNotContain(line => line.Contains(NewPassword, StringComparison.Ordinal) || line.Contains(factory.OwnerPassword, StringComparison.Ordinal));
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task A_wrong_current_password_is_401_changes_nothing_and_is_audited()
    {
        await factory.RestoreOwnerAsync();
        var since = factory.Clock.GetUtcNow();
        try
        {
            var signedIn = await factory.SignInAsync(Ct);
            var other = await factory.SignInAsync(Ct);

            using var response = await ChangePasswordAsync(signedIn.Client, "definitely-wrong", NewPassword);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.invalid_credentials");
            (await other.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await LoginClientAsync(ApiFactory.OwnerEmail, factory.OwnerPassword)).ShouldNotBeNull();
            (await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().CountAsync(e => e.Event == AuditEventType.StepUpFailed && e.At >= since, Ct))).ShouldBe(1);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task Wrong_current_passwords_count_toward_the_same_lockout_as_sign_in()
    {
        await factory.RestoreOwnerAsync();
        try
        {
            var signedIn = await factory.SignInAsync(Ct);
            for (var i = 0; i < 4; i++)
            {
                (await ChangePasswordAsync(signedIn.Client, "wrong", NewPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            using var locking = await ChangePasswordAsync(signedIn.Client, "wrong", NewPassword);
            using var duringLock = await ChangePasswordAsync(signedIn.Client, factory.OwnerPassword, NewPassword);
            using var signIn = await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);

            locking.StatusCode.ShouldBe((HttpStatusCode)423);
            var (code, retry) = await locking.ReadProblemAsync(Ct);
            code.ShouldBe("auth.locked");
            retry.ShouldNotBeNull().ShouldBeInRange(890, 900);
            duringLock.StatusCode.ShouldBe((HttpStatusCode)423);
            signIn.StatusCode.ShouldBe((HttpStatusCode)423);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("123456789")]
    public async Task A_too_short_new_password_is_400_with_a_field_message_and_does_not_count_as_a_failure(string? next)
    {
        await factory.RestoreOwnerAsync();
        try
        {
            var signedIn = await factory.SignInAsync(Ct);

            using var response = await ChangePasswordAsync(signedIn.Client, factory.OwnerPassword, next);

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.password_invalid");
            (await response.ReadFieldErrorsAsync(Ct)).ShouldContainKey("newPassword");
            (await factory.WithDbAsync(db => db.OwnerAccounts.AsNoTracking().Select(o => o.FailedAttempts).SingleAsync(Ct))).ShouldBe(0);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task A_new_password_equal_to_the_current_one_is_400_unchanged()
    {
        await factory.RestoreOwnerAsync();
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await ChangePasswordAsync(signedIn.Client, factory.OwnerPassword, factory.OwnerPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.password_unchanged");
        (await response.ReadFieldErrorsAsync(Ct)).ShouldContainKey("newPassword");
        (await LoginClientAsync(ApiFactory.OwnerEmail, factory.OwnerPassword)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Update_profile_without_step_up_is_403_and_changes_nothing()
    {
        await factory.RestoreOwnerAsync();
        var signedIn = await factory.SignInAsync(Ct);

        using var response = await UpdateProfileAsync(signedIn.Client, "Asha Rao", "asha@example.invalid");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.step_up_required");
        var account = await (await signedIn.Client.GetAsync("/api/auth/account", Ct)).ReadAsync<AccountResponse>(Ct);
        account.Email.ShouldBe(ApiFactory.OwnerEmail);
    }

    [Fact]
    public async Task Update_profile_needs_a_session_and_a_csrf_token()
    {
        var signedIn = await factory.SignInElevatedAsync(Ct);
        var noCsrf = factory.CreateAnonymousClient();
        noCsrf.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        var anonymous = await UpdateProfileAsync(factory.CreateAnonymousClient(), "Asha Rao", "asha@example.invalid");
        var refused = await UpdateProfileAsync(noCsrf, "Asha Rao", "asha@example.invalid");

        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ReadProblemAsync(Ct)).Code.ShouldBe("auth.csrf_invalid");
    }

    [Fact]
    public async Task Update_profile_after_step_up_returns_the_account_and_the_session_reflects_it_at_once()
    {
        await factory.RestoreOwnerAsync();
        var since = factory.Clock.GetUtcNow();
        try
        {
            var elevated = await factory.SignInElevatedAsync(Ct);
            var other = await factory.SignInAsync(Ct);

            using var response = await UpdateProfileAsync(elevated.Client, "  Asha Rao ", " Asha@Example.Invalid ");

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var account = await response.ReadAsync<AccountResponse>(Ct);
            account.Email.ShouldBe("asha@example.invalid");
            account.DisplayName.ShouldBe("Asha Rao");
            var session = await (await elevated.Client.GetAsync("/api/auth/session", Ct)).ReadAsync<SessionInfo>(Ct);
            session.User.ShouldBe(new UserInfo("asha@example.invalid", "Asha Rao"));
            var otherSession = await (await other.Client.GetAsync("/api/auth/session", Ct)).ReadAsync<SessionInfo>(Ct);
            otherSession.User.ShouldBe(new UserInfo("asha@example.invalid", "Asha Rao")); // other sessions stay valid and see the new name too
            (await LoginClientAsync("asha@example.invalid", factory.OwnerPassword)).ShouldNotBeNull();
            using var oldEmail = await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
            oldEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            var audited = await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().Where(e => e.Event == AuditEventType.ProfileUpdated && e.At >= since).ToListAsync(Ct));
            audited.Count.ShouldBe(1);
            audited[0].Detail.ShouldBe("changed: email, displayName");
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Theory]
    [InlineData("Asha Rao", "no-at-sign", "auth.owner_email_invalid", "email")]
    [InlineData("Asha Rao", "", "auth.owner_email_invalid", "email")]
    [InlineData("", "asha@example.invalid", "auth.owner_name_invalid", "displayName")]
    [InlineData("   ", "asha@example.invalid", "auth.owner_name_invalid", "displayName")]
    public async Task Update_profile_with_invalid_values_is_400_with_a_field_message(string name, string email, string expectedCode, string expectedField)
    {
        var elevated = await factory.SignInElevatedAsync(Ct);

        using var response = await UpdateProfileAsync(elevated.Client, name, email);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe(expectedCode);
        (await response.ReadFieldErrorsAsync(Ct)).ShouldContainKey(expectedField);
    }

    [Fact]
    public async Task Saving_unchanged_values_is_200_and_writes_no_audit_entry()
    {
        await factory.RestoreOwnerAsync();
        var elevated = await factory.SignInElevatedAsync(Ct);
        var account = await (await elevated.Client.GetAsync("/api/auth/account", Ct)).ReadAsync<AccountResponse>(Ct);
        var since = factory.Clock.GetUtcNow();

        using var response = await UpdateProfileAsync(elevated.Client, account.DisplayName, account.Email);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().CountAsync(e => e.Event == AuditEventType.ProfileUpdated && e.At >= since, Ct))).ShouldBe(0);
    }
}
