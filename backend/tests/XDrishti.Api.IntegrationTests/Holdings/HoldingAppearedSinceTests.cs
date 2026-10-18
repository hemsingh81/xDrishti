using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Api.IntegrationTests.Holdings;

/// <summary>
/// The question "did a holding or lot appear after this moment?" (<see cref="IHoldingStore.AnyAppearedSinceAsync"/>) on the real database: true when a holding was first seen, or a lot was created, strictly after the moment, for that account only. The broker history run asks it with the
/// end of the previous run plus a margin of one second (its own rebuild stamps the lots it makes a moment after its start), so a holding stamped inside the margin is not news.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class HoldingAppearedSinceTests(ApiFactory factory) : IAsyncLifetime
{
    // These tests write accounts straight into the tables and reset at the start of the test that needs it; the rows must not outlive the class.
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await factory.ResetAccountsAsync();

    private const string AccountId = "00000000-0000-7000-8000-00000000a201";

    private const string OtherAccountId = "00000000-0000-7000-8000-00000000a202";

    private const string Bucket = "00000000-0000-7000-8000-00000000c201";

    private static readonly Guid Account = Guid.Parse(AccountId);

    /// <summary>The end of the previous run, as the database stores it (microseconds).</summary>
    private static readonly DateTimeOffset Finished = new(2026, 10, 10, 10, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string AccountSql(string id, string slug, string client) =>
        $"""
        INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
        VALUES ('{id}', '{slug}', '{slug}', 'Dhan', '{client}', current_date, '#1A2B3C', now(), now(), false, false, false, 200);
        """;

    private static string HoldingSql(string id, string account, string security, string firstSeen) =>
        $"""
        INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
        VALUES ('{id}', '{account}', '{Bucket}', 'ALL', '{security}', 'S{security}', 'S{security}', 10, 1, 1, 'open', timestamptz '{firstSeen}', timestamptz '{firstSeen}');
        """;

    private static string LotSql(string holding, string createdAt) =>
        $"""
        INSERT INTO portfolio.holding_lots (id, holding_id, acquired_on, qty, unit_cost, source, uses_default_date, created_at, estimated)
        VALUES (gen_random_uuid(), '{holding}', '2025-01-01', 1, 1, 'import', false, timestamptz '{createdAt}', false);
        """;

    private async Task<NpgsqlConnection> SeededAsync()
    {
        await factory.ResetAccountsAsync();
        var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await AppRoleProbe.ExecuteAsync(connection, AccountSql(AccountId, "probe-appeared", "1000000201") + AccountSql(OtherAccountId, "probe-appeared-2", "1000000202"), Ct);
        await AppRoleProbe.ExecuteAsync(connection, $"INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES ('{Bucket}', 'B', now());", Ct);

        // an old holding with an old lot: the baseline, nothing appeared after the run
        await AppRoleProbe.ExecuteAsync(connection, HoldingSql("00000000-0000-7000-8000-00000000d201", AccountId, "1", "2026-10-10 08:00:00+00") + LotSql("00000000-0000-7000-8000-00000000d201", "2026-10-10 08:00:00+00"), Ct);
        return connection;
    }

    private async Task<bool> AppearedAsync(Guid account, DateTimeOffset since)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IHoldingStore>().AnyAppearedSinceAsync(account, since, Ct);
    }

    [Fact]
    public async Task Nothing_appeared_when_every_holding_and_lot_is_older_than_the_moment()
    {
        await using var admin = await SeededAsync();

        (await AppearedAsync(Account, Finished)).ShouldBeFalse();
        (await AppearedAsync(Guid.NewGuid(), Finished)).ShouldBeFalse(); // an account without holdings
    }

    [Fact]
    public async Task A_holding_first_seen_after_the_moment_appeared_and_one_stamped_exactly_at_it_did_not()
    {
        await using var admin = await SeededAsync();
        await AppRoleProbe.ExecuteAsync(admin, HoldingSql("00000000-0000-7000-8000-00000000d202", AccountId, "2", "2026-10-10 10:00:00.5+00"), Ct);

        (await AppearedAsync(Account, Finished)).ShouldBeTrue(); // 0.5 s after the run
        (await AppearedAsync(Account, Finished.AddMilliseconds(500))).ShouldBeFalse(); // strictly after: the same instant is not news
    }

    [Fact]
    public async Task The_margin_of_one_second_hides_what_the_runs_own_rebuild_stamped_just_after_it_ended()
    {
        await using var admin = await SeededAsync();
        await AppRoleProbe.ExecuteAsync(admin, HoldingSql("00000000-0000-7000-8000-00000000d202", AccountId, "2", "2026-10-10 10:00:00.4+00"), Ct);
        var margin = TimeSpan.FromSeconds(1); // BrokerHistoryPipeline.RebuiltLotsMargin

        (await AppearedAsync(Account, Finished)).ShouldBeTrue(); // without the margin it would look like a holding that appeared after the run
        (await AppearedAsync(Account, Finished + margin)).ShouldBeFalse(); // with it, it does not
        await AppRoleProbe.ExecuteAsync(admin, HoldingSql("00000000-0000-7000-8000-00000000d203", AccountId, "3", "2026-10-10 10:00:01.5+00"), Ct);
        (await AppearedAsync(Account, Finished + margin)).ShouldBeTrue(); // a holding that really came later is still seen
    }

    [Fact]
    public async Task A_lot_created_after_the_moment_makes_it_true_even_for_an_old_holding()
    {
        await using var admin = await SeededAsync();
        await AppRoleProbe.ExecuteAsync(admin, LotSql("00000000-0000-7000-8000-00000000d201", "2026-10-10 10:00:02+00"), Ct);

        (await AppearedAsync(Account, Finished + TimeSpan.FromSeconds(1))).ShouldBeTrue();
        (await AppearedAsync(Account, Finished + TimeSpan.FromSeconds(2))).ShouldBeFalse();
    }

    [Fact]
    public async Task What_appeared_in_another_account_does_not_count()
    {
        await using var admin = await SeededAsync();
        await AppRoleProbe.ExecuteAsync(admin, HoldingSql("00000000-0000-7000-8000-00000000d204", OtherAccountId, "4", "2026-10-11 09:00:00+00") + LotSql("00000000-0000-7000-8000-00000000d204", "2026-10-11 09:00:00+00"), Ct);

        (await AppearedAsync(Account, Finished)).ShouldBeFalse();
        (await AppearedAsync(Guid.Parse(OtherAccountId), Finished)).ShouldBeTrue();
    }
}
