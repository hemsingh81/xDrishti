using Microsoft.EntityFrameworkCore;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Accounts;

/// <summary>The rules the database itself enforces (they must hold even if a request slips past the application checks), and the seeded rows.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class AccountsDatabaseRulesTests(ApiFactory factory) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await factory.ResetAccountsAsync();

    public async ValueTask DisposeAsync() => await factory.ResetAccountsAsync();

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(Ct);
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync(Ct);
    }

    private Task InsertAccountAsync(string slug, bool primary = false, bool standby = false, bool enabled = false, bool archived = false) =>
        ExecuteAsync(
            """
            INSERT INTO accounts.broker_accounts
                (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, archived_at, data_enabled, data_primary, data_standby, live_feed_budget)
            VALUES
                (gen_random_uuid(), @slug, @slug, 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), CASE WHEN @archived THEN now() END, @enabled, @primary, @standby, 200)
            """,
            ("slug", slug), ("primary", primary), ("standby", standby), ("enabled", enabled || primary || standby), ("archived", archived));

    private Task InsertBucketAsync(string name, bool linked) =>
        ExecuteAsync(
            """
            INSERT INTO accounts.portfolio_buckets (id, name, created_at, default_for_account_id)
            VALUES (gen_random_uuid(), @name, now(), CASE WHEN @linked THEN (SELECT id FROM accounts.broker_accounts WHERE slug = 'owner-of-buckets') END)
            """,
            ("name", name), ("linked", linked));

    private static async Task ShouldViolateAsync(Func<Task> action, string sqlState, string constraint)
    {
        var error = await Should.ThrowAsync<PostgresException>(action);
        error.SqlState.ShouldBe(sqlState);
        error.ConstraintName.ShouldBe(constraint);
    }

    [Fact]
    public async Task Only_one_active_primary_Data_account_can_exist()
    {
        await InsertAccountAsync("first", primary: true);

        await ShouldViolateAsync(() => InsertAccountAsync("second", primary: true), PostgresErrorCodes.UniqueViolation, "ux_broker_accounts_data_primary");
    }

    [Fact]
    public async Task An_archived_primary_does_not_count_and_many_non_primary_accounts_are_fine()
    {
        await InsertAccountAsync("old", primary: true, archived: true);
        await InsertAccountAsync("current", primary: true);
        await InsertAccountAsync("plain_one");
        await InsertAccountAsync("plain_two");

        (await factory.WithDbAsync(db => db.BrokerAccounts.CountAsync(Ct))).ShouldBe(4);
    }

    [Fact]
    public async Task Only_one_active_standby_Data_account_can_exist()
    {
        await InsertAccountAsync("first", standby: true);

        await ShouldViolateAsync(() => InsertAccountAsync("second", standby: true), PostgresErrorCodes.UniqueViolation, "ux_broker_accounts_data_standby");
    }

    [Fact]
    public async Task An_account_cannot_be_primary_and_standby_and_neither_without_the_Data_role()
    {
        await ShouldViolateAsync(() => InsertAccountAsync("both", primary: true, standby: true), PostgresErrorCodes.CheckViolation, "ck_broker_accounts_data_role");
        await ShouldViolateAsync(
            () => ExecuteAsync("""
                INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, api_key_issued_on, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES (gen_random_uuid(), 'disabled', 'disabled', 'Dhan', '1000000001', current_date, '#1A2B3C', now(), now(), false, true, false, 200)
                """),
            PostgresErrorCodes.CheckViolation,
            "ck_broker_accounts_data_role");
    }

    [Fact]
    public async Task Slugs_are_unique_among_active_accounts_only()
    {
        await InsertAccountAsync("same");

        await ShouldViolateAsync(() => InsertAccountAsync("same"), PostgresErrorCodes.UniqueViolation, "ux_broker_accounts_slug_active");
        await ExecuteAsync("UPDATE accounts.broker_accounts SET archived_at = now() WHERE slug = 'same'");
        await InsertAccountAsync("same"); // an archived account frees its name
    }

    [Fact]
    public async Task Credential_references_are_gone_and_the_vault_table_and_key_date_exist_and_the_connection_has_no_unused_columns()
    {
        (await ScalarAsync("SELECT count(*) FROM information_schema.columns WHERE table_schema = 'accounts' AND column_name = 'credential_ref'")).ShouldBe(0L);
        (await ScalarAsync("SELECT count(*) FROM pg_indexes WHERE indexname = 'ux_broker_accounts_credential_ref_active'")).ShouldBe(0L);
        (await ScalarAsync("SELECT data_type FROM information_schema.columns WHERE table_schema = 'accounts' AND table_name = 'broker_accounts' AND column_name = 'api_key_issued_on'")).ShouldBe("date");
        (await ScalarAsync("SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'accounts' AND table_name = 'broker_accounts' AND column_name = 'client_id'")).ShouldBe("NO");
        (await ScalarAsync("SELECT is_nullable FROM information_schema.columns WHERE table_schema = 'accounts' AND table_name = 'broker_accounts' AND column_name = 'api_key_issued_on'")).ShouldBe("NO");
        (await ScalarAsync("""
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'accounts' AND ((table_name = 'broker_connections' AND column_name IN ('source', 'last_checked_at'))
                OR (table_name = 'pending_connects' AND column_name IN ('broker', 'state_hash')))
            """)).ShouldBe(0L);
        (await ScalarAsync("""
            SELECT string_agg(column_name || ':' || data_type, ',' ORDER BY ordinal_position) FROM information_schema.columns
            WHERE table_schema = 'accounts' AND table_name = 'broker_secrets'
            """)).ShouldBe("account_id:uuid,name:character varying,ciphertext:bytea,key_version:integer,updated_at:timestamp with time zone");
        (await ScalarAsync("""
            SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conrelid = 'accounts.broker_secrets'::regclass AND contype = 'p'
            """)).ShouldBe("PRIMARY KEY (account_id, name)");
    }

    [Fact]
    public async Task Secrets_go_with_their_account()
    {
        await InsertAccountAsync("owner_of_secrets");
        await ExecuteAsync("""
            INSERT INTO accounts.broker_secrets (account_id, name, ciphertext, key_version, updated_at)
            SELECT id, 'api_key', '\x00'::bytea, 1, now() FROM accounts.broker_accounts WHERE slug = 'owner_of_secrets'
            """);

        await ShouldViolateAsync(
            () => ExecuteAsync("INSERT INTO accounts.broker_secrets (account_id, name, ciphertext, key_version, updated_at) VALUES (gen_random_uuid(), 'api_key', '\\x00'::bytea, 1, now())"),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_broker_secrets_broker_accounts_account_id");
        await ExecuteAsync("DELETE FROM accounts.broker_accounts WHERE slug = 'owner_of_secrets'");

        (await ScalarAsync("SELECT count(*) FROM accounts.broker_secrets")).ShouldBe(0L);
    }

    [Fact]
    public async Task Risk_profile_and_bucket_names_are_unique_ignoring_case()
    {
        await ShouldViolateAsync(
            () => ExecuteAsync("""
                INSERT INTO accounts.risk_profiles (id, name, is_seeded, created_at, updated_at, risk_per_trade_pct, max_capital_per_trade_pct, max_new_entries_per_day, max_open_positions,
                    max_total_risk_pct, max_per_sector, max_pair_correlation, max_intraday, max_swing, daily_loss_r, daily_loss_pct, weekly_loss_r, weekly_loss_action)
                VALUES (gen_random_uuid(), 'STANDARD', false, now(), now(), 0.5, 25, 3, 5, 3, 2, 0.7, 2, 2, 2, 1.5, 5, 'HalveSize')
                """),
            PostgresErrorCodes.UniqueViolation,
            "ux_risk_profiles_name_lower");
        await ExecuteAsync("INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES (gen_random_uuid(), 'Growth', now())");
        await ShouldViolateAsync(
            () => ExecuteAsync("INSERT INTO accounts.portfolio_buckets (id, name, created_at) VALUES (gen_random_uuid(), 'growth', now())"),
            PostgresErrorCodes.UniqueViolation,
            "ux_portfolio_buckets_name_lower");
    }

    [Fact]
    public async Task The_migrations_seed_the_standard_and_conservative_profiles_and_no_buckets()
    {
        var profiles = await factory.WithDbAsync(db => db.RiskProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync(Ct));
        var buckets = await factory.WithDbAsync(db => db.PortfolioBuckets.AsNoTracking().Select(b => b.Name).ToListAsync(Ct));

        profiles.Select(p => p.Name).ShouldBe(["Conservative", "Standard"]);
        profiles.ShouldAllBe(p => p.IsSeeded);
        var standard = profiles.Single(p => p.Name == "Standard");
        (standard.RiskPerTradePct, standard.MaxCapitalPerTradePct, standard.MaxNewEntriesPerDay, standard.MaxOpenPositions, standard.MaxTotalRiskPct, standard.MaxPerSector)
            .ShouldBe((0.5m, 25m, 3, 5, 3.0m, 2));
        (standard.MaxPairCorrelation, standard.MaxIntraday, standard.MaxSwing, standard.DailyLossR, standard.DailyLossPct, standard.WeeklyLossR)
            .ShouldBe((0.70m, 2, 2, 2m, 1.5m, 5m));
        standard.WeeklyLossAction.ShouldBe(Domain.Accounts.WeeklyLossAction.HalveSize);
        var conservative = profiles.Single(p => p.Name == "Conservative");
        (conservative.RiskPerTradePct, conservative.MaxNewEntriesPerDay, conservative.MaxOpenPositions, conservative.DailyLossPct)
            .ShouldBe((0.25m, 2, 3, 1.0m));
        (conservative.MaxCapitalPerTradePct, conservative.MaxTotalRiskPct, conservative.DailyLossR).ShouldBe((25m, 3.0m, 2m)); // inherited from Standard
        buckets.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_account_has_at_most_one_default_bucket_and_custom_buckets_may_repeat_the_empty_link()
    {
        await InsertAccountAsync("owner-of-buckets");

        await InsertBucketAsync("Owner · Portfolio", linked: true);
        await ShouldViolateAsync(() => InsertBucketAsync("Another name", linked: true), PostgresErrorCodes.UniqueViolation, "ux_portfolio_buckets_default_for_account");
        await InsertBucketAsync("Custom one", linked: false);
        await InsertBucketAsync("Custom two", linked: false);

        (await ScalarAsync("SELECT count(*) FROM accounts.portfolio_buckets")).ShouldBe(3L);
    }

    [Fact]
    public async Task A_default_bucket_must_belong_to_an_existing_account_and_keeps_it_from_being_deleted()
    {
        await ShouldViolateAsync(
            () => ExecuteAsync("INSERT INTO accounts.portfolio_buckets (id, name, created_at, default_for_account_id) VALUES (gen_random_uuid(), 'Orphan · Portfolio', now(), gen_random_uuid())"),
            PostgresErrorCodes.ForeignKeyViolation,
            "fk_portfolio_buckets_broker_accounts_default_for_account_id");
        await InsertAccountAsync("goes-away");
        await ExecuteAsync("INSERT INTO accounts.portfolio_buckets (id, name, created_at, default_for_account_id) SELECT gen_random_uuid(), 'Goes · Portfolio', now(), id FROM accounts.broker_accounts WHERE slug = 'goes-away'");

        await ShouldViolateAsync(
            () => ExecuteAsync("DELETE FROM accounts.broker_accounts WHERE slug = 'goes-away'"),
            PostgresErrorCodes.RestrictViolation,
            "fk_portfolio_buckets_broker_accounts_default_for_account_id");
        await ExecuteAsync("DELETE FROM accounts.portfolio_buckets WHERE name = 'Goes · Portfolio'");
        await ExecuteAsync("DELETE FROM accounts.broker_accounts WHERE slug = 'goes-away'");

        (await ScalarAsync("SELECT count(*) FROM accounts.broker_accounts WHERE slug = 'goes-away'")).ShouldBe(0L);
    }

    [Fact]
    public async Task Deleting_a_profile_that_only_archived_accounts_use_clears_their_reference()
    {
        var client = (await factory.SignInElevatedAsync(Ct)).Client;
        using var created = await client.PostJsonAsync("/api/risk-profiles", AccountsApi.RiskProfile("Temporary"), Ct);
        var profile = await created.ReadAsCreatedAsync<Application.Accounts.RiskProfiles.RiskProfileView>(Ct);
        var account = await client.CreateAccountAsync(AccountsApi.NewAccount(trading: AccountsApi.Trading(profile.Id)), Ct);
        await client.DeleteAsync($"/api/accounts/{account.Id}", Ct);

        (await client.DeleteAsync($"/api/risk-profiles/{profile.Id}", Ct)).StatusCode.ShouldBe(System.Net.HttpStatusCode.NoContent);

        (await factory.WithDbAsync(db => db.BrokerAccounts.AsNoTracking().SingleAsync(a => a.Id == account.Id, Ct))).Trading.RiskProfileId.ShouldBeNull();
    }
}
