using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.Auth;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Auth;

/// <summary>AC 8: provisioning is idempotent; owner-reset replaces the password and ends all sessions.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class OwnerBootstrapTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Provisioning_again_leaves_the_single_owner_untouched()
    {
        var before = await factory.WithDbAsync(db => db.OwnerAccounts.AsNoTracking().SingleAsync(Ct));

        (await OwnerBootstrap.ProvisionAsync(factory.Services, Ct)).ShouldBe(0);

        var after = await factory.WithDbAsync(db => db.OwnerAccounts.AsNoTracking().SingleAsync(Ct));
        after.Id.ShouldBe(before.Id);
        after.PasswordHash.ShouldBe(before.PasswordHash);
        (await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Owner_reset_replaces_the_password_and_signs_out_everywhere()
    {
        var signedIn = await factory.SignInAsync(Ct);
        var newPassword = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));
        using var reset = factory.WithWebHostBuilder(b => b.UseSetting("Auth:Owner:Password", newPassword));
        try
        {
            (await OwnerBootstrap.ResetAsync(reset.Services, Ct)).ShouldBe(0);

            (await signedIn.Client.GetAsync("/api/auth/verify", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            var client = factory.CreateAnonymousClient();
            (await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await client.LoginAsync(ApiFactory.OwnerEmail, newPassword, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
            (await factory.WithDbAsync(db => db.AuditEvents.AsNoTracking().AnyAsync(e => e.Event == Domain.Auth.AuditEventType.OwnerPasswordReset, Ct))).ShouldBeTrue();
        }
        finally
        {
            await factory.RestoreOwnerAsync();
        }
    }

    [Fact]
    public async Task Owner_reset_without_the_secrets_fails_and_changes_nothing()
    {
        using var noSecrets = factory.WithWebHostBuilder(b => b.UseSetting("Auth:Owner:Password", string.Empty));

        (await OwnerBootstrap.ResetAsync(noSecrets.Services, Ct)).ShouldBe(1);

        (await factory.CreateAnonymousClient().LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
