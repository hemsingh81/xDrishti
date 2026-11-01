using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.OptionContracts;

/// <summary>Spec P14-01 Contract and criterion 7: <c>AddOptionContracts</c> creates schema <c>options</c> with its three tables and the seeded rule epochs, agrees with the model, and <c>Down</c> drops the schema.</summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionMigrationTests(ApiFactory factory)
{
    private const string Before = "20261101085914_GuardConfirmTicketReissue";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<string>> ListAsync(NpgsqlConnection connection, string sql)
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

    [Fact]
    public async Task The_migration_creates_the_three_tables_their_keys_and_checks_and_the_unconfirmed_epoch_seeds()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_options_up_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'options' ORDER BY 1")).ShouldBe(["backfill_probe_ack", "backfill_switch", "chain_recorder_switch", "chain_requests", "chain_sessions", "chain_snapshots", "contracts", "fetch_log", "lot_sizes", "market_epochs", "rolling_bars"]); // the three of P14-01, the four of P14-02 and the four of P14-03
        (await ListAsync(connection, "SELECT column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'options' AND table_name = 'contracts' ORDER BY ordinal_position")).ShouldBe(
        [
            "security_id|bigint|NO", "underlying_id|uuid|NO", "instrument|character varying|NO", "expiry|date|NO", "strike_px|integer|NO", "side|character|NO", "expiry_kind|character varying|YES",
            "lot_size_seen|integer|NO", "tick_px|integer|YES", "first_seen_on|date|NO", "last_seen_on|date|NO",
        ]);
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE connamespace = 'options'::regnamespace AND contype <> 'n' AND conname NOT LIKE '%chain!_%' ESCAPE '!' AND conname NOT SIMILAR TO '%(rolling_bars|fetch_log|backfill_switch|backfill_probe_ack)%' ORDER BY 1")).ShouldBe(
        [
            "ck_contracts_expiry_kind", "ck_contracts_instrument", "ck_contracts_lot_size_seen", "ck_contracts_seen", "ck_contracts_side", "ck_contracts_strike_px", "ck_contracts_tick_px",
            "ck_lot_sizes_confirmed", "ck_lot_sizes_lot_size", "ck_lot_sizes_source", "ck_market_epochs_confirmed", "fk_contracts_underlying", "fk_lot_sizes_underlying",
            "pk_contracts", "pk_lot_sizes", "pk_market_epochs",
        ]);
        (await ListAsync(connection, "SELECT indexdef FROM pg_indexes WHERE indexname = 'ux_contracts_underlying_expiry_strike_side'")).Single().ShouldContain("UNIQUE INDEX");
        (await ListAsync(connection, "SELECT code || '|' || effective_from || '|' || confirmed || '|' || (source_url LIKE 'https://%') FROM options.market_epochs ORDER BY effective_from, code")).ShouldBe(
        [
            "min_contract_value_15_lakh|2024-11-20|false|true", "weekly_index_one_per_exchange|2024-11-20|false|true", "upfront_premium|2025-02-01|false|true",
            "nifty_expiry_tuesday|2025-09-01|false|true", "index_lot_revision|2025-12-30|false|true", "stt_fo_raise|2026-04-01|false|true",
        ]);
        scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
    }

    [Fact]
    public async Task Down_drops_the_schema_and_up_again_is_clean()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(factory, "xd_options_down_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var migrator = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>().GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        await migrator.MigrateAsync(Before, Ct);

        (await ListAsync(connection, "SELECT nspname::text FROM pg_namespace WHERE nspname = 'options'")).ShouldBeEmpty();

        await migrator.MigrateAsync(null, Ct);

        (await ListAsync(connection, "SELECT table_name FROM information_schema.tables WHERE table_schema = 'options' ORDER BY 1")).ShouldBe(["backfill_probe_ack", "backfill_switch", "chain_recorder_switch", "chain_requests", "chain_sessions", "chain_snapshots", "contracts", "fetch_log", "lot_sizes", "market_epochs", "rolling_bars"]); // the three of P14-01, the four of P14-02 and the four of P14-03
    }
}
