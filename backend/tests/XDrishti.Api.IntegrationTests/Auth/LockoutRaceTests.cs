using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Domain.Auth;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>
/// Review finding: failure counting must be race-safe. Parallel requests (each from its own address, so the per-address
/// rate limit does not interfere) must neither lose failures nor slip a correct password past the lock.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class LockoutRaceTests(ApiFactory factory)
{
    private const int Parallel = 12;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Task<HttpResponseMessage[]> LoginInParallelAsync(params string[] passwords) =>
        Task.WhenAll(passwords.Select(password => Task.Run(() => factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, password, Ct), Ct)));

    [Fact]
    public async Task Parallel_wrong_passwords_lock_after_exactly_five_counted_failures()
    {
        await factory.RestoreOwnerAsync();
        var since = factory.Clock.GetUtcNow();
        try
        {
            var responses = await LoginInParallelAsync([.. Enumerable.Repeat("wrong", Parallel)]);

            responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(4); // the fifth failure locks
            responses.Count(r => r.StatusCode == (HttpStatusCode)423).ShouldBe(Parallel - 4);
            responses.Select(r => r.StatusCode).ShouldAllBe(s => s == HttpStatusCode.Unauthorized || s == (HttpStatusCode)423);

            var (owner, events) = await ReadStateAsync(since);
            owner.FailedAttempts.ShouldBe(OwnerAccount.MaxFailedAttempts);
            owner.IsLocked(factory.Clock.GetUtcNow()).ShouldBeTrue();
            events.Count(e => e == AuditEventType.LoginFailed).ShouldBe(OwnerAccount.MaxFailedAttempts);
            events.Count(e => e == AuditEventType.AccountLocked).ShouldBe(1);
            events.Count(e => e == AuditEventType.LoginRefusedLocked).ShouldBe(Parallel - OwnerAccount.MaxFailedAttempts);

            var afterwards = await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
            afterwards.StatusCode.ShouldBe((HttpStatusCode)423);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task A_correct_password_in_a_parallel_mix_cannot_bypass_the_lock(int round)
    {
        round.ShouldBeGreaterThan(0);
        await factory.RestoreOwnerAsync();
        try
        {
            var sequential = factory.CreateAnonymousClient();
            for (var i = 0; i < OwnerAccount.MaxFailedAttempts - 1; i++)
            {
                (await sequential.LoginAsync(ApiFactory.OwnerEmail, "wrong", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            }

            // Four failures stored: any wrong attempt that is saved before the correct one locks the account.
            var passwords = Enumerable.Repeat("wrong", Parallel - 1).Append(factory.OwnerPassword).ToArray();
            var responses = await LoginInParallelAsync(passwords);

            var correct = responses[^1];
            var wrong = responses[..^1];
            if (correct.StatusCode == HttpStatusCode.OK)
            {
                // It was saved first: the account was open, and the wrong attempts then locked it after four more counted failures.
                wrong.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).ShouldBe(OwnerAccount.MaxFailedAttempts - 1);
            }
            else
            {
                // A wrong attempt locked the account first: the correct password was refused and nothing but 423 was answered.
                correct.StatusCode.ShouldBe((HttpStatusCode)423);
                ApiFactory.SessionCookieValue(correct).ShouldBeNull();
                wrong.ShouldAllBe(r => r.StatusCode == (HttpStatusCode)423);
            }

            var (owner, _) = await ReadStateAsync(DateTimeOffset.MinValue);
            owner.IsLocked(factory.Clock.GetUtcNow()).ShouldBeTrue();
            owner.FailedAttempts.ShouldBe(OwnerAccount.MaxFailedAttempts);
            (await sequential.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct)).StatusCode.ShouldBe((HttpStatusCode)423);
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task Parallel_correct_passwords_all_sign_in()
    {
        await factory.RestoreOwnerAsync();

        var responses = await LoginInParallelAsync([.. Enumerable.Repeat(factory.OwnerPassword, Parallel)]);

        responses.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
    }

    private Task<(OwnerAccount Owner, AuditEventType[] Events)> ReadStateAsync(DateTimeOffset since) =>
        factory.WithDbAsync(async db => (
            await db.OwnerAccounts.AsNoTracking().SingleAsync(Ct),
            await db.AuditEvents.AsNoTracking().Where(e => e.At >= since).Select(e => e.Event).ToArrayAsync(Ct)));
}
