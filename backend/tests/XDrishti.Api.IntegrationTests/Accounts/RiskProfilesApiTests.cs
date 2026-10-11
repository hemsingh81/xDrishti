using System.Net;
using Microsoft.EntityFrameworkCore;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Accounts.Buckets;
using XDrishti.Application.Accounts.RiskProfiles;
using XDrishti.Domain.Accounts;

namespace XDrishti.Api.IntegrationTests.Accounts;

[Collection(ApiTestSuite.Name)]
public sealed class RiskProfilesApiTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async Task<RiskProfileView> CreateProfileAsync(HttpClient client, string name = "Aggressive") =>
        await (await client.PostJsonAsync("/api/risk-profiles", AccountsApi.RiskProfile(name), Ct)).ReadAsCreatedAsync<RiskProfileView>(Ct);

    [Fact]
    public async Task The_seeded_profiles_are_listed_with_the_documented_values_and_no_users()
    {
        var client = (await factory.SignInAsync(Ct)).Client;

        var list = await (await client.GetAsync("/api/risk-profiles", Ct)).ReadAsync<List<RiskProfileView>>(Ct);

        list.Select(p => (p.Name, p.IsSeeded, p.AccountCount)).ShouldBe([("Conservative", true, 0), ("Standard", true, 0)]);
        var standard = list.Single(p => p.Name == "Standard");
        standard.Id.ShouldBe(AccountsApi.StandardProfileId);
        (standard.RiskPerTradePct, standard.MaxNewEntriesPerDay, standard.MaxOpenPositions, standard.DailyLossPct, standard.WeeklyLossAction).ShouldBe((0.5m, 3, 5, 1.5m, "halve_size"));
        var conservative = list.Single(p => p.Name == "Conservative");
        (conservative.RiskPerTradePct, conservative.MaxNewEntriesPerDay, conservative.MaxOpenPositions, conservative.DailyLossPct).ShouldBe((0.25m, 2, 3, 1.0m));
    }

    [Fact]
    public async Task A_profile_is_created_listed_and_audited_with_the_actor()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        var created = await CreateProfileAsync(client);

        created.Name.ShouldBe("Aggressive");
        created.IsSeeded.ShouldBeFalse();
        created.RiskPerTradePct.ShouldBe(1.0m);
        (await (await client.GetAsync("/api/risk-profiles", Ct)).ReadAsync<List<RiskProfileView>>(Ct)).ShouldContain(p => p.Id == created.Id);
        var entry = await factory.WithDbAsync(db => db.AccountEvents.AsNoTracking().SingleAsync(e => e.Event == AccountEventType.RiskProfileCreated, Ct));
        (entry.AccountId, entry.Actor).ShouldBe((null, ApiFactory.OwnerEmail));
    }

    [Fact]
    public async Task Invalid_values_are_400_with_the_field_names()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        using var outOfRange = await client.PostJsonAsync("/api/risk-profiles", AccountsApi.RiskProfile("Too Risky", riskPerTradePct: 2.5m), Ct);
        using var missing = await client.PostJsonAsync("/api/risk-profiles", new { name = "Empty" }, Ct);

        outOfRange.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await outOfRange.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.invalid");
        (await outOfRange.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["riskPerTradePct"]);
        var errors = await missing.ReadFieldErrorsAsync(Ct);
        errors.Keys.ShouldContain("riskPerTradePct");
        errors.Keys.ShouldContain("weeklyLossAction");
    }

    [Fact]
    public async Task A_duplicate_name_is_409_ignoring_case()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;

        using var response = await client.PostJsonAsync("/api/risk-profiles", AccountsApi.RiskProfile("STANDARD"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.conflict");
    }

    [Fact]
    public async Task Updating_replaces_the_values_and_unknown_or_clashing_requests_are_refused()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var created = await CreateProfileAsync(client);

        var updated = await (await client.PutJsonAsync($"/api/risk-profiles/{created.Id}", AccountsApi.RiskProfile("Aggressive 2", 1.5m), Ct)).ReadAsync<RiskProfileView>(Ct);
        using var clash = await client.PutJsonAsync($"/api/risk-profiles/{created.Id}", AccountsApi.RiskProfile("standard"), Ct);
        using var unknown = await client.PutJsonAsync($"/api/risk-profiles/{Guid.NewGuid()}", AccountsApi.RiskProfile(), Ct);

        updated.Name.ShouldBe("Aggressive 2");
        updated.RiskPerTradePct.ShouldBe(1.5m);
        clash.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await factory.WithDbAsync(db => db.AccountEvents.AsNoTracking().SingleAsync(e => e.Event == AccountEventType.RiskProfileUpdated, Ct))).Detail
            .ShouldBe("Risk profile 'Aggressive 2': name 'Aggressive' -> 'Aggressive 2'; riskPerTradePct 1 -> 1.5");
    }

    [Fact]
    public async Task A_seeded_profile_can_be_edited_but_not_deleted()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var original = (await (await client.GetAsync("/api/risk-profiles", Ct)).ReadAsync<List<RiskProfileView>>(Ct)).Single(p => p.Name == "Conservative");
        try
        {
            var edited = await (await client.PutJsonAsync($"/api/risk-profiles/{original.Id}", AccountsApi.RiskProfile("Conservative", 0.2m), Ct)).ReadAsync<RiskProfileView>(Ct);
            using var delete = await client.DeleteAsync($"/api/risk-profiles/{original.Id}", Ct);

            edited.RiskPerTradePct.ShouldBe(0.2m);
            delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await delete.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.conflict");
        }
        finally
        {
            // Every original value back (the view has the input's fields), so tests that read the seeded values later are not affected.
            (await client.PutJsonAsync($"/api/risk-profiles/{original.Id}", original, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task A_profile_in_use_cannot_be_deleted_and_the_count_follows_the_accounts()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        var profile = await CreateProfileAsync(client);
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(trading: AccountsApi.Trading(profile.Id)), Ct);

        var counted = (await (await client.GetAsync("/api/risk-profiles", Ct)).ReadAsync<List<RiskProfileView>>(Ct)).Single(p => p.Id == profile.Id);
        using var refused = await client.DeleteAsync($"/api/risk-profiles/{profile.Id}", Ct);
        await client.PutJsonAsync($"/api/accounts/{account.Id}/trading", AccountsApi.Trading(AccountsApi.StandardProfileId), Ct);
        using var deleted = await client.DeleteAsync($"/api/risk-profiles/{profile.Id}", Ct);

        counted.AccountCount.ShouldBe(1);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.ReadProblemAsync(Ct)).Code.ShouldBe("accounts.conflict");
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/risk-profiles/{profile.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await (await client.GetAsync("/api/risk-profiles", Ct)).ReadAsync<List<RiskProfileView>>(Ct)).Single(p => p.Id == AccountsApi.StandardProfileId).AccountCount.ShouldBe(1);
    }

    [Fact]
    public async Task Buckets_are_seeded_and_new_ones_can_be_added_without_step_up()
    {
        var client = (await factory.SignInAsync(Ct)).Client;

        var seeded = await (await client.GetAsync("/api/portfolio-buckets", Ct)).ReadAsync<List<BucketView>>(Ct);
        using var added = await client.PostJsonAsync("/api/portfolio-buckets", new { name = "  Growth Ideas " }, Ct);
        using var duplicate = await client.PostJsonAsync("/api/portfolio-buckets", new { name = "growth ideas" }, Ct);
        using var invalid = await client.PostJsonAsync("/api/portfolio-buckets", new { name = "a/b" }, Ct);

        seeded.Select(b => b.Name).ShouldBe(["Trading", "MyLongTerm", "MyChoice"], ignoreOrder: true);
        (await added.ReadAsCreatedAsync<BucketView>(Ct)).Name.ShouldBe("Growth Ideas");
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.ReadFieldErrorsAsync(Ct)).Keys.ShouldBe(["name"]);
        (await client.PutJsonAsync("/api/accounts/" + Guid.NewGuid() + "/portfolio", new { defaultBucket = "Growth Ideas" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
