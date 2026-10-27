using System.Net;
using System.Net.Http.Json;
using Npgsql;
using XDrishti.Api.Auth;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Application.Market.Refresh;
using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Baskets;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Market;

/// <summary>
/// The instrument master works with the production runtime role xd_app (DML only, no DDL): the test creates the role, recreates the production default
/// privileges (deploy/db/init/10-roles.sh) in a scratch database, runs the real migrations there as the owner and then runs a complete refresh
/// through the API connected as xd_app (temporary table, binary COPY, advisory lock, merge).
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class MarketTableGrantsTests(ApiFactory factory)
{
    private const string ScratchDatabase = "xd_market_grants_probe";

    private const string AppPassword = "app-role-probe-only";

    /// <summary>Tables whose rows are never deleted by the runtime role (design 06 §3.4).</summary>
    private static readonly string[] KeptTables = ["baskets", "basket_members", "tracked_instruments"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_app_role_has_plain_DML_on_the_market_tables_but_no_DDL_and_a_refresh_works_as_that_role()
    {
        var admin = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString);
        var scratch = new NpgsqlConnectionStringBuilder(factory.AdminConnectionString) { Database = ScratchDatabase, Pooling = false };
        await CreateScratchDatabaseAsync(admin, scratch);
        try
        {
            await using (var migrated = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", scratch.ConnectionString)))
            {
                await DatabaseMigrator.MigrateAsync(migrated.Services, Ct);
            }

            await using (var owner = new NpgsqlConnection(scratch.ConnectionString))
            {
                await owner.OpenAsync(Ct);
                (await ScalarAsync(owner, "SELECT count(*) FROM pg_extension WHERE extname = 'pg_trgm'")).ShouldBe(1L);
                await CheckSeededBasketsAsync(owner);
            }

            await using (var connection = new NpgsqlConnection(scratch.ConnectionString))
            {
                await connection.OpenAsync(Ct);
                await ExecuteAsync(connection, "SET ROLE xd_app");
                await CheckPrivilegesAsync(connection);
            }

            await RunRefreshAsAppRoleAsync(new NpgsqlConnectionStringBuilder(scratch.ConnectionString) { Username = "xd_app", Password = AppPassword });
        }
        finally
        {
            await DropScratchDatabaseAsync(admin);
        }
    }

    private static async Task CheckPrivilegesAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, """
            INSERT INTO market.instruments (id, canonical_symbol, exchange, exchange_segment, type, name, display_name, lot_size, underlying_symbol, fno_eligible, first_seen_at, last_seen_at)
            VALUES ('00000000-0000-7000-8000-00000000a001', 'NSE:PROBE', 'NSE', 'NSE_EQ', 'share', 'PROBE', 'Probe', 1, 'PROBE', false, now(), now())
            """);
        await ExecuteAsync(connection, "UPDATE market.instruments SET retired_at = now() WHERE canonical_symbol = 'NSE:PROBE'");
        await ExecuteAsync(connection, "INSERT INTO market.instrument_broker_ids (instrument_id, broker, broker_id, segment) VALUES ('00000000-0000-7000-8000-00000000a001', 'dhan', '1', 'NSE_EQ')");
        await ExecuteAsync(connection, "UPDATE market.instrument_broker_ids SET broker_id = '2'");
        await ExecuteAsync(connection, "DELETE FROM market.instrument_broker_ids");

        // Tracking: plain DML, and the table's own rules hold for the runtime role as well.
        await ExecuteAsync(connection, "INSERT INTO market.tracked_instruments (instrument_id, active, active_since, backfill_requested_at, tracked_by) VALUES ('00000000-0000-7000-8000-00000000a001', true, now(), now(), 'probe')");
        await ExecuteAsync(connection, "UPDATE market.tracked_instruments SET active = false, deactivated_at = now()");
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "UPDATE market.tracked_instruments SET active = true"), PostgresErrorCodes.CheckViolation); // active needs deactivated_at cleared
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "UPDATE market.tracked_instruments SET data_from = '2026-01-02', data_to = '2026-01-01'"), PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DELETE FROM market.instruments"), PostgresErrorCodes.RestrictViolation); // tracked instruments are never deleted
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "INSERT INTO market.tracked_instruments (instrument_id, active, active_since, tracked_by) VALUES ('00000000-0000-7000-8000-00000000a999', true, now(), 'probe')"), PostgresErrorCodes.ForeignKeyViolation);

        // Baskets: plain DML, and the tables' own rules hold for the runtime role as well.
        await CheckBasketRulesAsync(connection);

        // Rows of baskets, members and tracking are kept for good: the runtime role cannot delete them (the owner clears the probe rows, as the test reset does).
        foreach (var table in KeptTables)
        {
            await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, $"DELETE FROM market.{table}"), PostgresErrorCodes.InsufficientPrivilege);
        }

        await AsOwnerAsync(connection, "DELETE FROM market.basket_members; DELETE FROM market.baskets WHERE name = 'Probe'; DELETE FROM market.tracked_instruments");
        (await ScalarAsync(connection, "SELECT count(*) FROM market.baskets")).ShouldBe(3L);
        await ExecuteAsync(connection, "DELETE FROM market.instruments");

        await InsertRunAsync(connection, "00000000-0000-7000-8000-00000000b001", "running");
        await ShouldBeRejectedAsync(connection, () => InsertRunAsync(connection, "00000000-0000-7000-8000-00000000b002", "running"), PostgresErrorCodes.UniqueViolation); // one running refresh
        await ExecuteAsync(connection, "UPDATE market.master_refreshes SET status = 'failed', finished_at = now(), phase = NULL");
        await InsertRunAsync(connection, "00000000-0000-7000-8000-00000000b003", "running"); // possible again once the first ended
        await ShouldBeRejectedAsync(connection, () => InsertRunAsync(connection, "00000000-0000-7000-8000-00000000b004", "weird"), PostgresErrorCodes.CheckViolation);
        await ExecuteAsync(connection, "DELETE FROM market.master_refreshes");

        // What the importer needs: a temporary table to copy into.
        await ExecuteAsync(connection, "CREATE TEMP TABLE probe_stage (a int) ON COMMIT DROP");
        (await ScalarAsync(connection, "SELECT pg_try_advisory_xact_lock(1)")).ShouldBe(true);

        // No DDL in the market schema.
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "CREATE TABLE market.intruder (a int)"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DROP TABLE market.instruments"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "ALTER TABLE market.instruments ADD COLUMN x int"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "TRUNCATE market.instruments"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DROP TABLE market.baskets"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "ALTER TABLE market.basket_members ADD COLUMN x int"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "CREATE INDEX ix_intruder ON market.baskets (name)"), PostgresErrorCodes.InsufficientPrivilege);

        foreach (var table in new[] { "instruments", "instrument_broker_ids", "master_refreshes", "tracked_instruments", "baskets", "basket_members" })
        {
            foreach (var privilege in new[] { "SELECT", "INSERT", "UPDATE", "DELETE" })
            {
                var expected = privilege != "DELETE" || !KeptTables.Contains(table);
                (await ScalarAsync(connection, $"SELECT has_table_privilege('xd_app', 'market.{table}', '{privilege}')")).ShouldBe(expected, $"{privilege} on {table}");
            }
        }
    }

    private async Task RunRefreshAsAppRoleAsync(NpgsqlConnectionStringBuilder asApp)
    {
        await using var host = factory.WithWebHostBuilder(b => b.UseSetting("Database:ConnectionString", asApp.ConnectionString));
        (await OwnerBootstrap.ProvisionAsync(host.Services)).ShouldBe(0);
        var client = host.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.18.250.88");
        using var login = await client.LoginAsync(ApiFactory.OwnerEmail, factory.OwnerPassword, Ct);
        login.EnsureSuccessStatusCode();
        var session = (await login.Content.ReadFromJsonAsync<XDrishti.Application.Auth.SessionInfo>(Ct))!;
        client.DefaultRequestHeaders.Add("Cookie", $"xd.sid={ApiFactory.SessionCookieValue(login)}");
        client.DefaultRequestHeaders.Add("X-CSRF-Token", session.CsrfToken);

        // The sample master holds October 2026 contracts that expire on the 27th: on a later real date this very refresh would retire them (60 added, 51 active), so the refresh runs on a pinned date before that.
        factory.Clock.Pin(new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero));
        using var started = await client.PostAsync("/api/instruments/refresh", Ct);
        started.StatusCode.ShouldBe(HttpStatusCode.Accepted, await started.Content.ReadAsStringAsync(Ct));
        await factory.RefreshJobs.RunPendingAsync(host.Services, Ct);
        factory.Clock.Pin(DateTimeOffset.UtcNow);

        using var statusResponse = await client.GetAsync("/api/instruments/refresh/status", Ct);
        var status = await statusResponse.ReadAsync<RefreshStatusView>(Ct);
        status.Last!.Status.ShouldBe("succeeded", status.Last.Error);
        status.Last.Added.ShouldBe(60);
        status.TotalActive.ShouldBe(60);

        // Tracking as the runtime role: track, list, untrack (the audit event goes to accounts.account_events).
        using var listed = await client.GetAsync("/api/instruments?q=NSE:TCS&type=share", Ct);
        var tcs = (await listed.ReadAsync<XDrishti.Application.Market.Search.InstrumentPage>(Ct)).Items.Single();
        using var tracked = await client.PostJsonAsync("/api/instruments/tracked", new { instrumentIds = new[] { tcs.Id } }, Ct);
        tracked.StatusCode.ShouldBe(HttpStatusCode.OK, await tracked.Content.ReadAsStringAsync(Ct));
        using var onlyTracked = await client.GetAsync("/api/instruments?tracked=true", Ct);
        (await onlyTracked.ReadAsync<XDrishti.Application.Market.Search.InstrumentPage>(Ct)).Items.Single().Id.ShouldBe(tcs.Id);
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri("/api/instruments/tracked", UriKind.Relative)) { Content = JsonContent.Create(new { instrumentIds = new[] { tcs.Id } }) };
        using var untracked = await client.SendAsync(request, Ct);
        untracked.StatusCode.ShouldBe(HttpStatusCode.OK, await untracked.Content.ReadAsStringAsync(Ct));

        // Baskets as the runtime role: the seeded ones are listed, an add tracks the instrument and saves the membership and both audit entries, then copy, reorder, remove, archive.
        using var seeded = await client.GetAsync("/api/baskets", Ct);
        var baskets = await seeded.ReadAsync<List<XDrishti.Application.Market.Baskets.BasketView>>(Ct);
        baskets.Select(b => b.Name).ShouldBe(["MyChoice", "MyLongTerm", "Best stocks"]);
        using var created = await client.PostJsonAsync("/api/baskets", new { name = "Probe", purpose = "watch" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var probe = (await created.Content.ReadFromJsonAsync<XDrishti.Application.Market.Baskets.BasketView>(Ct))!;
        using var added = await client.PostJsonAsync($"/api/baskets/{baskets[0].Id}/members", new { instrumentIds = new[] { tcs.Id }, note = "probe" }, Ct);
        (await added.ReadAsync<XDrishti.Application.Market.Baskets.AddMembersResult>(Ct)).ShouldBe(new XDrishti.Application.Market.Baskets.AddMembersResult(1, 1, 0, 1, 1));
        using var copied = await client.PostJsonAsync("/api/baskets/transfer", new { fromBasketId = baskets[0].Id, toBasketId = probe.Id, instrumentIds = new[] { tcs.Id }, mode = "copy" }, Ct);
        (await copied.ReadAsync<XDrishti.Application.Market.Baskets.TransferResult>(Ct)).Added.ShouldBe(1);
        using var reordered = await client.PutJsonAsync("/api/baskets/order", new { ids = new[] { probe.Id, baskets[2].Id, baskets[1].Id, baskets[0].Id } }, Ct);
        reordered.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reordered.Content.ReadAsStringAsync(Ct));
        using var members = await client.GetAsync($"/api/baskets/{probe.Id}/members?asOf={IndiaTime.Today(factory.Clock.GetUtcNow()):yyyy-MM-dd}", Ct);
        (await members.ReadAsync<XDrishti.Application.Market.Baskets.BasketMembers>(Ct)).Items.ShouldHaveSingleItem().Instrument.Id.ShouldBe(tcs.Id);
        using var removeRequest = new HttpRequestMessage(HttpMethod.Delete, new Uri($"/api/baskets/{probe.Id}/members", UriKind.Relative)) { Content = JsonContent.Create(new { instrumentIds = new[] { tcs.Id } }) };
        using var removed = await client.SendAsync(removeRequest, Ct);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK, await removed.Content.ReadAsStringAsync(Ct));
        using var archived = await client.DeleteAsync($"/api/baskets/{probe.Id}", Ct);
        archived.StatusCode.ShouldBe(HttpStatusCode.NoContent, await archived.Content.ReadAsStringAsync(Ct));
        using var history = await client.GetAsync($"/api/baskets/{probe.Id}/history", Ct);
        (await history.ReadAsync<XDrishti.Application.Market.Baskets.BasketHistory>(Ct)).Items.Count.ShouldBe(2);
    }

    /// <summary>The migration created the three baskets of the design (the values of <see cref="DefaultBaskets"/>), empty and active.</summary>
    private static async Task CheckSeededBasketsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT id, name, colour, purpose, position, description, archived_at, revision FROM market.baskets ORDER BY position", connection);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var rows = new List<(Guid Id, string Name, string Colour, string Purpose, int Position)>();
        while (await reader.ReadAsync(Ct))
        {
            reader.IsDBNull(5).ShouldBeTrue();
            reader.IsDBNull(6).ShouldBeTrue();
            reader.GetInt32(7).ShouldBe(0);
            rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4)));
        }

        rows.ShouldBe([.. DefaultBaskets.All.Select(b => (b.Id, b.Name, b.Colour, b.Purpose.ToCode(), b.Position))]);
    }

    private static async Task CheckBasketRulesAsync(NpgsqlConnection connection)
    {
        const string Basket = "00000000-0000-7000-8000-00000000c001";
        const string Instrument = "00000000-0000-7000-8000-00000000a001";
        string Insert(string id, string name, string colour = "#112233", string purpose = "trading") =>
            $"INSERT INTO market.baskets (id, name, colour, purpose, position, created_at, revision) VALUES ('{id}', '{name}', '{colour}', '{purpose}', 7, now(), 0)";
        string Member(string id, string basket = Basket) =>
            $"INSERT INTO market.basket_members (id, basket_id, instrument_id, position, valid_from, added_by) VALUES ('{id}', '{basket}', '{Instrument}', 0, now(), 'probe')";

        // The seeded names are taken among active baskets, ignoring case; the colour and the purpose are checked.
        await ExecuteAsync(connection, Insert(Basket, "Probe"));
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, Insert("00000000-0000-7000-8000-00000000c002", "PROBE")), PostgresErrorCodes.UniqueViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, Insert("00000000-0000-7000-8000-00000000c002", "mychoice")), PostgresErrorCodes.UniqueViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, Insert("00000000-0000-7000-8000-00000000c002", "Other", colour: "red")), PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, Insert("00000000-0000-7000-8000-00000000c002", "Other", colour: "#ABCDEF")), PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, Insert("00000000-0000-7000-8000-00000000c002", "Other", purpose: "weird")), PostgresErrorCodes.CheckViolation);

        // An instrument is in a basket once at a time; removing keeps the row and frees the place; the stay cannot end before it began.
        await ExecuteAsync(connection, Member("00000000-0000-7000-8000-00000000d001"));
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, Member("00000000-0000-7000-8000-00000000d002")), PostgresErrorCodes.UniqueViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "UPDATE market.basket_members SET valid_to = now() - interval '1 day', removed_by = 'probe'"), PostgresErrorCodes.CheckViolation);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "UPDATE market.basket_members SET valid_to = now()"), PostgresErrorCodes.CheckViolation); // removed_by goes with valid_to
        await ExecuteAsync(connection, "UPDATE market.basket_members SET valid_to = now(), removed_by = 'probe'");
        await ExecuteAsync(connection, Member("00000000-0000-7000-8000-00000000d002"));
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, Member("00000000-0000-7000-8000-00000000d003", "00000000-0000-7000-8000-00000000c999")), PostgresErrorCodes.ForeignKeyViolation);

        // The same instrument in another basket is fine (many-to-many); baskets and instruments with history are never deleted.
        await ExecuteAsync(connection, Member("00000000-0000-7000-8000-00000000d004", DefaultBaskets.MyChoice.Id.ToString()));
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DELETE FROM market.baskets WHERE name = 'Probe'"), PostgresErrorCodes.InsufficientPrivilege);
        await ShouldBeRejectedAsync(connection, () => ExecuteAsync(connection, "DELETE FROM market.instruments"), PostgresErrorCodes.RestrictViolation);

        // An archived basket frees its name.
        await ExecuteAsync(connection, "UPDATE market.baskets SET archived_at = now() WHERE name = 'Probe'");
        await ExecuteAsync(connection, Insert("00000000-0000-7000-8000-00000000c003", "Probe"));
    }

    private static Task InsertRunAsync(NpgsqlConnection connection, string id, string status) => ExecuteAsync(connection, $$"""
        INSERT INTO market.master_refreshes (id, "trigger", "force", requested_by, started_at, status, phase, rows_read, added, updated, retired, revived, unchanged,
            skipped_by_scope, collisions, sector_matched, sector_unmatched, fno_underlyings, total_active, kept_by_type, warnings, rules_version, sectors_applied)
        VALUES ('{{id}}', 'manual', false, 'probe', now(), '{{status}}', 'downloading', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, '{}'::jsonb, '[]'::jsonb, 1, false)
        """);

    private static async Task ShouldBeRejectedAsync(NpgsqlConnection connection, Func<Task> action, string sqlState)
    {
        // A failed statement aborts the surrounding transaction only when there is one; these run autocommit.
        var error = await Should.ThrowAsync<PostgresException>(action);
        error.SqlState.ShouldBe(sqlState);
        connection.State.ShouldBe(System.Data.ConnectionState.Open);
    }

    private static async Task CreateScratchDatabaseAsync(NpgsqlConnectionStringBuilder admin, NpgsqlConnectionStringBuilder scratch)
    {
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(Ct);
            await ExecuteAsync(connection, "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN CREATE ROLE xd_app NOLOGIN; END IF; END $$");
            await ExecuteAsync(connection, $"ALTER ROLE xd_app LOGIN PASSWORD '{AppPassword}'");
            await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {ScratchDatabase} WITH (FORCE)");
            await ExecuteAsync(connection, $"CREATE DATABASE {ScratchDatabase}");
            await ExecuteAsync(connection, $"GRANT CONNECT ON DATABASE {ScratchDatabase} TO xd_app");
        }

        await using var scratchConnection = new NpgsqlConnection(scratch.ConnectionString);
        await scratchConnection.OpenAsync(Ct);
        await ExecuteAsync(scratchConnection, "ALTER DEFAULT PRIVILEGES GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO xd_app");
        await ExecuteAsync(scratchConnection, "ALTER DEFAULT PRIVILEGES GRANT USAGE, SELECT ON SEQUENCES TO xd_app");
    }

    private static async Task DropScratchDatabaseAsync(NpgsqlConnectionStringBuilder admin)
    {
        await using var connection = new NpgsqlConnection(admin.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {ScratchDatabase} WITH (FORCE)");
        await ExecuteAsync(connection, "ALTER ROLE xd_app NOLOGIN");
    }

    /// <summary>Runs the statements with the owner's rights on a connection that has switched to xd_app, then switches back.</summary>
    private static async Task AsOwnerAsync(NpgsqlConnection connection, string sql)
    {
        await ExecuteAsync(connection, "RESET ROLE");
        await ExecuteAsync(connection, sql);
        await ExecuteAsync(connection, "SET ROLE xd_app");
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
}
