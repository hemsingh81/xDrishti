using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>
/// What the migration AddDefaultPortfolioBuckets (P1-16e) does to a database that has the old state: the three preset buckets, accounts that
/// point at them, and a history_from date. The database is built up to the migration before it, filled, and then migrated.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class DefaultBucketMigrationTests(ApiFactory factory)
{
    private const string Scratch = "xd_default_bucket_migration_probe";

    private const string BeforeMigration = "20261011113155_AddInstrumentMaster";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Existing_portfolio_accounts_get_their_bucket_the_presets_are_removed_and_history_from_is_dropped()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = Scratch, Pooling = false };
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {Scratch} WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE {Scratch}");
        }

        try
        {
            await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString));
            using var scope = host.Services.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();

            await migrator.MigrateAsync(BeforeMigration, Ct);
            await using var connection = new NpgsqlConnection(scratch.ConnectionString);
            await connection.OpenAsync(Ct);
            (await ScalarAsync(connection, "SELECT count(*) FROM accounts.portfolio_buckets")).ShouldBe(3L); // the old presets
            await FillOldStateAsync(connection);

            await migrator.MigrateAsync(null, Ct);

            await AssertMigratedAsync(connection);
        }
        finally
        {
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {Scratch} WITH (FORCE)");
        }
    }

    private static async Task FillOldStateAsync(NpgsqlConnection connection)
    {
        // a: the owner's account (Portfolio on, preset Trading, a history date); b: role off with preset MyChoice; c: Portfolio on, no bucket;
        // d: archived with the role on and preset MyLongTerm; e/f: same name (slugs differ), both Portfolio on; g: Portfolio on with a custom bucket;
        // h: name differs only by case from an existing bucket; i: a custom bucket is named exactly like its would-be default bucket;
        // j: role off but owns a custom bucket (its choice must survive).
        await ExecuteAsync(connection, """
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, archived_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES
                ('00000000-0000-7000-8000-00000000a001', 'dhan-main', 'Dhan · Main', 'Dhan', '1000000001', current_date, '#1A2B3C', now() - interval '6 day', now(), NULL, false, false, false, 200),
                ('00000000-0000-7000-8000-00000000a002', 'role-off', 'Role Off', 'Dhan', '1000000002', current_date, '#1A2B3C', now() - interval '5 day', now(), NULL, false, false, false, 200),
                ('00000000-0000-7000-8000-00000000a003', 'no-bucket', 'No Bucket', 'Dhan', '1000000003', current_date, '#1A2B3C', now() - interval '4 day', now(), NULL, false, false, false, 200),
                ('00000000-0000-7000-8000-00000000a004', 'archived', 'Archived One', 'Dhan', '1000000004', current_date, '#1A2B3C', now() - interval '3 day', now(), now(), false, false, false, 200),
                ('00000000-0000-7000-8000-00000000a005', 'twin', 'Twin', 'Dhan', '1000000005', current_date, '#1A2B3C', now() - interval '2 day', now(), NULL, false, false, false, 200),
                ('00000000-0000-7000-8000-00000000a006', 'twin-2', 'Twin', 'Dhan', '1000000006', current_date, '#1A2B3C', now() - interval '1 day', now(), NULL, false, false, false, 200),
                ('00000000-0000-7000-8000-00000000a007', 'custom', 'Custom Pick', 'Dhan', '1000000007', current_date, '#1A2B3C', now(), now(), NULL, false, false, false, 200),
                ('00000000-0000-7000-8000-00000000a008', 'alpha', 'Alpha', 'Dhan', '1000000008', current_date, '#1A2B3C', now() + interval '1 day', now(), NULL, false, false, false, 200),
                ('00000000-0000-7000-8000-00000000a009', 'beta', 'Beta', 'Dhan', '1000000009', current_date, '#1A2B3C', now() + interval '2 day', now(), NULL, false, false, false, 200),
                ('00000000-0000-7000-8000-00000000a010', 'role-off-custom', 'Role Off Custom', 'Dhan', '1000000010', current_date, '#1A2B3C', now() + interval '3 day', now(), NULL, false, false, false, 200)
            """);
        await ExecuteAsync(connection, """
            INSERT INTO accounts.portfolio_buckets (id, name, created_at)
            VALUES
                ('00000000-0000-7000-8000-0000000000c1', 'Growth', now()),
                ('00000000-0000-7000-8000-0000000000c2', 'ALPHA · portfolio', now()),
                ('00000000-0000-7000-8000-0000000000c3', 'Beta · Portfolio', now())
            """);
        await ExecuteAsync(connection, """
            INSERT INTO accounts.portfolio_settings (account_id, enabled, default_bucket, history_from, include_in_totals)
            VALUES
                ('00000000-0000-7000-8000-00000000a001', true,  'Trading',    '2024-04-01', false),
                ('00000000-0000-7000-8000-00000000a002', false, 'MyChoice',   NULL,         true),
                ('00000000-0000-7000-8000-00000000a003', true,  NULL,         NULL,         true),
                ('00000000-0000-7000-8000-00000000a004', true,  'MyLongTerm', NULL,         true),
                ('00000000-0000-7000-8000-00000000a005', true,  NULL,         NULL,         true),
                ('00000000-0000-7000-8000-00000000a006', true,  NULL,         NULL,         true),
                ('00000000-0000-7000-8000-00000000a007', true,  'Growth',     NULL,         true),
                ('00000000-0000-7000-8000-00000000a008', true,  NULL,         NULL,         true),
                ('00000000-0000-7000-8000-00000000a009', true,  NULL,         NULL,         true),
                ('00000000-0000-7000-8000-00000000a010', false, 'Growth',     NULL,         true)
            """);
    }

    private static async Task AssertMigratedAsync(NpgsqlConnection connection)
    {
        // The presets are gone; the custom bucket stays; Portfolio accounts that are active (and only those) got their default bucket.
        var buckets = await RowsAsync(connection, "SELECT name || '|' || COALESCE(default_for_account_id::text, '-') FROM accounts.portfolio_buckets ORDER BY name");
        buckets.ShouldBe(
        [
            "Dhan · Main · Portfolio|00000000-0000-7000-8000-00000000a001",
            "Custom Pick · Portfolio|00000000-0000-7000-8000-00000000a007",
            "Growth|-",
            "ALPHA · portfolio|-",
            "Alpha · Portfolio 2|00000000-0000-7000-8000-00000000a008",
            "Beta · Portfolio|-",
            "Beta · Portfolio 2|00000000-0000-7000-8000-00000000a009",
            "No Bucket · Portfolio|00000000-0000-7000-8000-00000000a003",
            "Twin · Portfolio|00000000-0000-7000-8000-00000000a005",
            "Twin · Portfolio 2|00000000-0000-7000-8000-00000000a006",
        ],
        ignoreOrder: true);

        // Accounts that pointed at a preset now use their own default bucket (NULL); a custom choice is kept.
        var settings = await RowsAsync(connection, "SELECT right(s.account_id::text, 4) || '|' || COALESCE(b.name, '-') || '|' || s.include_in_totals FROM accounts.portfolio_settings s LEFT JOIN accounts.portfolio_buckets b ON b.id = s.default_bucket_id");
        settings.ShouldBe(["a001|-|false", "a002|-|true", "a003|-|true", "a004|-|true", "a005|-|true", "a006|-|true", "a007|Growth|true", "a008|-|true", "a009|-|true", "a010|Growth|true"], ignoreOrder: true);

        // history_from and the name-based default_bucket are gone.
        (await ScalarAsync(connection, "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'accounts' AND table_name = 'portfolio_settings' AND column_name IN ('history_from', 'default_bucket')")).ShouldBe(0L);

        // The migration is visible in the audit trail of the accounts it touched.
        var events = await RowsAsync(connection, "SELECT right(account_id::text, 4) || '|' || event || '|' || actor FROM accounts.account_events ORDER BY 1");
        events.ShouldBe(
        [
            "a001|BucketCreated|migration",
            "a001|PortfolioUpdated|migration", // role on and active: audited; b (role off) and d (archived) are re-pointed silently
            "a003|BucketCreated|migration",
            "a005|BucketCreated|migration",
            "a006|BucketCreated|migration",
            "a007|BucketCreated|migration",
            "a008|BucketCreated|migration",
            "a009|BucketCreated|migration",
        ],
        ignoreOrder: true);

        // The link is unique: a second default bucket for an account is refused.
        var error = await Should.ThrowAsync<PostgresException>(() => ExecuteAsync(
            connection, "INSERT INTO accounts.portfolio_buckets (id, name, created_at, default_for_account_id) VALUES (gen_random_uuid(), 'Dup', now(), '00000000-0000-7000-8000-00000000a001')"));
        error.ConstraintName.ShouldBe("ux_portfolio_buckets_default_for_account");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private static async Task<List<string>> RowsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<string>();
        while (await reader.ReadAsync(Ct))
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
