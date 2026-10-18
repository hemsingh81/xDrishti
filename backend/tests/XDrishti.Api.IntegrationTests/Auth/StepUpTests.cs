using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Auth;
using XDrishti.Domain.Auth;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>P1-02: password re-entry (step-up) for sensitive endpoints. The risk-profile create endpoint is the probe: it is sensitive and has no side effects when refused.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class StepUpTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public async ValueTask DisposeAsync() => await factory.ResetAccountsAsync();

    private static Task<HttpResponseMessage> SensitiveCallAsync(HttpClient client, string name = "Step Up Probe") =>
        client.PostJsonAsync("/api/risk-profiles", AccountsApi.RiskProfile(name), Ct);

    [Fact]
    public async Task A_sensitive_endpoint_without_step_up_answers_403_step_up_required()
    {
        var signedIn = await factory.SignInAsync(Ct);

        var response = await SensitiveCallAsync(signedIn.Client);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.step_up_required");
    }

    [Fact]
    public async Task The_correct_password_elevates_for_five_minutes_and_the_endpoint_then_works()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var before = factory.Clock.GetUtcNow();

        using var stepUp = await signedIn.Client.StepUpAsync(factory.OwnerPassword, Ct);

        stepUp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await stepUp.Content.ReadFromJsonAsync<StepUpResult>(Ct))!;
        result.ElevatedUntil.ShouldBeInRange(before.AddMinutes(5).AddSeconds(-2), before.AddMinutes(5).AddSeconds(5));
        (await SensitiveCallAsync(signedIn.Client)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_session_endpoint_reports_the_elevation_window()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var before = await (await signedIn.Client.GetAsync("/api/auth/session", Ct)).ReadAsync<SessionInfo>(Ct);
        await signedIn.Client.StepUpAsync(factory.OwnerPassword, Ct);

        var after = await (await signedIn.Client.GetAsync("/api/auth/session", Ct)).ReadAsync<SessionInfo>(Ct);

        before.ElevatedUntil.ShouldBeNull();
        after.ElevatedUntil.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_elevation_ends_after_five_minutes()
    {
        var signedIn = await factory.SignInElevatedAsync(Ct);
        try
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(4));
            (await SensitiveCallAsync(signedIn.Client, "Within The Window")).StatusCode.ShouldBe(HttpStatusCode.Created);

            factory.Clock.Advance(TimeSpan.FromMinutes(1).Add(TimeSpan.FromSeconds(5)));

            var response = await SensitiveCallAsync(signedIn.Client, "After The Window");
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.step_up_required");
            (await signedIn.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent); // still signed in
        }
        finally
        {
            await factory.ResetClockAsync();
        }
    }

    [Fact]
    public async Task Stepping_up_again_opens_a_fresh_window()
    {
        var signedIn = await factory.SignInElevatedAsync(Ct);
        try
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(4));
            (await signedIn.Client.StepUpAsync(factory.OwnerPassword, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

            factory.Clock.Advance(TimeSpan.FromMinutes(4));

            (await SensitiveCallAsync(signedIn.Client)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }
        finally
        {
            await factory.ResetClockAsync();
        }
    }

    [Fact]
    public async Task Logging_out_ends_the_elevation_and_a_new_session_starts_without_one()
    {
        var first = await factory.SignInElevatedAsync(Ct);
        await first.Client.PostAsync("/api/auth/logout", Ct);

        (await SensitiveCallAsync(first.Client)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var second = await factory.SignInAsync(Ct);
        var response = await SensitiveCallAsync(second.Client);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.step_up_required");
    }

    [Fact]
    public async Task Elevation_belongs_to_one_session_only()
    {
        var elevated = await factory.SignInElevatedAsync(Ct);
        var other = await factory.SignInAsync(Ct);

        (await SensitiveCallAsync(elevated.Client)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await SensitiveCallAsync(other.Client, "Other Session")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_wrong_password_is_401_invalid_credentials_audited_and_does_not_elevate()
    {
        await factory.RestoreOwnerAsync();
        var since = factory.Clock.GetUtcNow();
        try
        {
            var signedIn = await factory.SignInAsync(Ct);

            using var response = await signedIn.Client.StepUpAsync("definitely-wrong", Ct);

            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.invalid_credentials");
            (await SensitiveCallAsync(signedIn.Client)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().CountAsync(e => e.Event == AuditEventType.StepUpFailed && e.At >= since, Ct))).ShouldBe(1);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task A_success_is_audited()
    {
        var since = factory.Clock.GetUtcNow();

        await factory.SignInElevatedAsync(Ct);

        (await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().AnyAsync(e => e.Event == AuditEventType.StepUpSucceeded && e.At >= since, Ct))).ShouldBeTrue();
    }

    [Fact]
    public async Task Wrong_passwords_count_toward_the_same_lockout_as_sign_in_and_a_locked_account_is_refused()
    {
        await factory.RestoreOwnerAsync();
        try
        {
            var signedIn = await factory.SignInAsync(Ct);
            for (var i = 0; i < 4; i++)
            {
                (await signedIn.Client.StepUpAsync("wrong", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            using var locking = await signedIn.Client.StepUpAsync("wrong", Ct);
            using var duringLock = await signedIn.Client.StepUpAsync(factory.OwnerPassword, Ct);
            using var signIn = await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);

            locking.StatusCode.ShouldBe((HttpStatusCode)423);
            var (code, retry) = await locking.ReadProblemAsync(Ct);
            code.ShouldBe("auth.locked");
            retry.ShouldNotBeNull().ShouldBeInRange(890, 900);
            duringLock.StatusCode.ShouldBe((HttpStatusCode)423);
            signIn.StatusCode.ShouldBe((HttpStatusCode)423); // the account is locked for sign-in too
            (await SensitiveCallAsync(signedIn.Client)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task Failed_sign_ins_and_failed_step_ups_share_one_counter()
    {
        await factory.RestoreOwnerAsync();
        try
        {
            var signedIn = await factory.SignInAsync(Ct);
            for (var i = 0; i < 3; i++)
            {
                await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, "wrong", Ct);
            }

            (await signedIn.Client.StepUpAsync("wrong", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // 4th failure
            (await signedIn.Client.StepUpAsync("wrong", Ct)).StatusCode.ShouldBe((HttpStatusCode)423); // 5th locks
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task Step_up_needs_a_session_and_a_csrf_token()
    {
        var anonymous = factory.CreateAnonymousClient();
        var signedIn = await factory.SignInAsync(Ct);
        var noCsrf = factory.CreateAnonymousClient();
        noCsrf.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        (await anonymous.StepUpAsync(factory.OwnerPassword, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var refused = await noCsrf.StepUpAsync(factory.OwnerPassword, Ct);

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await refused.ReadProblemAsync(Ct)).Code.ShouldBe("auth.csrf_invalid");
    }

    [Fact]
    public async Task An_empty_or_missing_password_is_refused_like_a_wrong_one()
    {
        await factory.RestoreOwnerAsync();
        try
        {
            var signedIn = await factory.SignInAsync(Ct);

            (await signedIn.Client.StepUpAsync(string.Empty, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await signedIn.Client.StepUpAsync(null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task An_unauthenticated_call_to_a_sensitive_endpoint_gets_401_before_403()
    {
        var response = await SensitiveCallAsync(factory.CreateAnonymousClient());

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_sensitive_call_without_the_csrf_token_gets_the_csrf_error_even_when_elevated()
    {
        var signedIn = await factory.SignInElevatedAsync(Ct);
        var noCsrf = factory.CreateAnonymousClient();
        noCsrf.DefaultRequestHeaders.Add("Cookie", signedIn.CookieHeader);

        var response = await SensitiveCallAsync(noCsrf);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("auth.csrf_invalid");
    }

    [Fact]
    public async Task Endpoints_that_are_not_sensitive_work_without_step_up()
    {
        var signedIn = await factory.SignInAsync(Ct);

        (await signedIn.Client.GetAsync("/api/accounts", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await signedIn.Client.GetAsync("/api/risk-profiles", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await signedIn.Client.PostJsonAsync("/api/portfolio-buckets", new { name = "Step Up Free" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
