using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.OptionContracts;

/// <summary>
/// Spec P14-01 criterion 3: on the dedicated server migrated by the owner with the init script's default privileges, the runtime role <c>xd_app</c> reads the three
/// option tables, inserts into all three, may update only <c>contracts.last_seen_on</c> (a column grant), and can delete or truncate nothing; no other role (<c>xd_exec</c>, <c>xd_mcp_read</c>, <c>xd_mcp_sql</c>) is granted.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class OptionGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static readonly string[] Tables = ["contracts", "lot_sizes", "market_epochs"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string?> SqlStateAsync(string connectionString, string sql)
    {
        try
        {
            await StoreKit.ExecuteAsync(connectionString, sql, Ct);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private async Task<bool> AskAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(server.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return (bool)(await command.ExecuteScalarAsync(Ct))!;
    }

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_updates_only_last_seen_on_and_never_deletes_or_truncates()
    {
        foreach (var table in Tables)
        {
            foreach (var (privilege, expected) in new[] { ("SELECT", true), ("INSERT", true), ("UPDATE", false), ("DELETE", false), ("TRUNCATE", false) })
            {
                (await AskAsync($"SELECT has_table_privilege('xd_app', 'options.{table}', '{privilege}')")).ShouldBe(expected, table + " " + privilege);
            }
        }

        (await AskAsync("SELECT has_column_privilege('xd_app', 'options.contracts', 'last_seen_on', 'UPDATE')")).ShouldBeTrue();
        foreach (var column in new[] { "security_id", "underlying_id", "instrument", "expiry", "strike_px", "side", "expiry_kind", "lot_size_seen", "tick_px", "first_seen_on" })
        {
            (await AskAsync($"SELECT has_column_privilege('xd_app', 'options.contracts', '{column}', 'UPDATE')")).ShouldBeFalse(column);
        }
    }

    [Fact]
    public async Task As_the_runtime_role_the_writes_the_import_needs_work_and_every_other_change_is_refused()
    {
        var instrument = Guid.CreateVersion7();
        await StoreKit.ExecuteAsync(server.Owner, $"INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES ('{instrument}', 'NSE:OPTPROBE', 'NSE', 'IDX_I', 'index', 'OPTPROBE', 'Probe', 1, 'OPTPROBE', false, now(), now())", Ct);

        (await SqlStateAsync(server.App, $"INSERT INTO options.contracts (security_id, underlying_id, instrument, expiry, strike_px, side, lot_size_seen, first_seen_on, last_seen_on) VALUES (990001, '{instrument}', 'OPTIDX', '2026-11-09', 100, 'CE', 65, '2026-10-11', '2026-10-11')")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.contracts SET last_seen_on = '2026-10-12' WHERE security_id = 990001")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.contracts SET strike_px = 200 WHERE security_id = 990001")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "UPDATE options.contracts SET lot_size_seen = 75")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM options.contracts")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "TRUNCATE options.contracts")).ShouldBe("42501");
        (await SqlStateAsync(server.App, $"INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, confirmed) VALUES ('{instrument}', '2026-10-11', 65, 'scrip_master', false)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.lot_sizes SET lot_size = 75")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM options.lot_sizes")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "INSERT INTO options.market_epochs (code, effective_from, description, confirmed) VALUES ('probe', '2026-10-11', 'probe', false)")).ShouldBeNull();
        (await SqlStateAsync(server.App, "UPDATE options.market_epochs SET confirmed = true")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM options.market_epochs")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "CREATE TABLE options.intruder (a int)")).ShouldBe("42501");

        await StoreKit.ExecuteAsync(server.Owner, $"DELETE FROM options.lot_sizes; DELETE FROM options.contracts; DELETE FROM options.market_epochs WHERE code = 'probe'; DELETE FROM market.instruments WHERE id = '{instrument}'", Ct);
    }

    [Fact]
    public async Task The_execution_and_the_read_only_roles_have_no_right_on_any_option_table()
    {
        foreach (var role in new[] { "xd_exec", "xd_mcp_read", "xd_mcp_sql" })
        {
            foreach (var table in Tables)
            {
                (await AskAsync($"SELECT CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{role}') THEN has_table_privilege('{role}', 'options.{table}', 'SELECT') OR has_table_privilege('{role}', 'options.{table}', 'INSERT') ELSE false END")).ShouldBeFalse(role + " " + table);
            }
        }
    }

    [Fact]
    public async Task An_nse_lot_row_is_inserted_by_the_runtime_role_never_confirmed_and_never_updated_or_deleted()
    {
        var instrument = Guid.CreateVersion7();
        await StoreKit.ExecuteAsync(server.Owner, $"INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at) VALUES ('{instrument}', 'NSE:NSEPROBE', 'NSE', 'NSE_EQ', 'share', 'NSEPROBE', 'Probe', 1, 'NSEPROBE', false, now(), now())", Ct);

        (await SqlStateAsync(server.App, $"INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, source_url, confirmed) VALUES ('{instrument}', '2026-11-12', 500, 'nse_mktlots', 'https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv', false)")).ShouldBeNull();
        (await SqlStateAsync(server.App, $"INSERT INTO options.lot_sizes (underlying_id, effective_from, lot_size, source, source_url, confirmed) VALUES ('{instrument}', '2026-11-12', 500, 'nse_mktlots', 'https://nsearchives.nseindia.com/content/fo/fo_mktlots.csv', true)")).ShouldBe("23514");
        (await SqlStateAsync(server.App, "UPDATE options.lot_sizes SET lot_size = 475 WHERE source = 'nse_mktlots'")).ShouldBe("42501");
        (await SqlStateAsync(server.App, "DELETE FROM options.lot_sizes WHERE source = 'nse_mktlots'")).ShouldBe("42501");

        await StoreKit.ExecuteAsync(server.Owner, $"DELETE FROM options.lot_sizes; DELETE FROM market.instruments WHERE id = '{instrument}'", Ct);
    }
}
