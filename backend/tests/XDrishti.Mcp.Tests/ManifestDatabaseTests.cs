using System.Text.RegularExpressions;
using Npgsql;
using XDrishti.Domain.Assistant;
using XDrishti.Mcp.Tests.Support;

namespace XDrishti.Mcp.Tests;

/// <summary>Spec P9-02 AC 2: the manifest and the live views of schema <c>aiv</c> agree both ways, and the views carry nothing the model must not see.</summary>
[Collection(ReadOnlyDatabaseSuite.Name)]
public sealed class ManifestDatabaseTests(ReadOnlyDatabase db)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<List<(string View, string Column, string Type)>> LiveColumnsAsync()
    {
        await using var connection = new NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT c.relname, a.attname, format_type(a.atttypid, a.atttypmod) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'aiv' AND a.attnum > 0 AND NOT a.attisdropped ORDER BY c.relname, a.attnum", connection);
        var rows = new List<(string, string, string)>();
        await using var reader = await command.ExecuteReaderAsync(Ct);
        while (await reader.ReadAsync(Ct))
        {
            rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    [Fact]
    public async Task The_live_columns_of_every_view_equal_the_manifest_in_name_and_order()
    {
        var live = (await LiveColumnsAsync()).GroupBy(c => c.View).ToDictionary(g => g.Key, g => g.Select(c => c.Column).ToList());

        live.Keys.Order().ShouldBe(DataManifest.Views.Select(v => v.Name).Order(), "views in the database and in the manifest");
        foreach (var view in DataManifest.Views)
        {
            live[view.Name].ShouldBe(view.Columns.Select(c => c.Name), $"columns of {view.Name}");
        }
    }

    [Fact]
    public async Task No_live_column_has_a_forbidden_name()
    {
        var pattern = new Regex(DataManifest.ForbiddenColumnPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

        var offenders = (await LiveColumnsAsync()).Where(c => pattern.IsMatch(c.Column)).Select(c => $"{c.View}.{c.Column}").ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_kind_of_every_column_fits_its_database_type()
    {
        var live = (await LiveColumnsAsync()).ToDictionary(c => (c.View, c.Column), c => c.Type);
        foreach (var view in DataManifest.Views)
        {
            foreach (var column in view.Columns)
            {
                var type = live[(view.Name, column.Name)];
                var isString = Regex.IsMatch(type, "^(text|character|character varying)(\\(\\d+\\))?(\\[\\])?$");
                var allowed = column.Kind switch
                {
                    ColumnKind.Text => isString,
                    ColumnKind.Number or ColumnKind.Amount => Regex.IsMatch(type, "^(numeric|integer|bigint|smallint|double precision|real)"),
                    ColumnKind.Date => type == "date",
                    ColumnKind.Timestamp => Regex.IsMatch(type, "^(timestamp with time zone|time without time zone|timestamp without time zone)"),
                    ColumnKind.Flag => type == "boolean",
                    _ => true,
                };
                allowed.ShouldBeTrue($"{view.Name}.{column.Name} is {column.Kind} but the database type is {type}");
                if (column.Kind is ColumnKind.Text)
                {
                    column.Class.ShouldBe(DataClass.U);
                }
            }
        }
    }

    [Fact]
    public async Task No_view_definition_reaches_a_table_the_model_must_not_see_or_a_one_minute_hypertable()
    {
        await using var connection = new NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT c.relname, pg_get_viewdef(c.oid) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'aiv' AND c.relkind = 'v'", connection);
        var definitions = new Dictionary<string, string>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                definitions[reader.GetString(0)] = reader.GetString(1);
            }
        }

        definitions.Count.ShouldBe(DataManifest.Views.Count);
        string[] forbidden = ["accounts.", "auth.", "platform.app_settings", "platform.__ef_migrations_history", "__ef_migrations_history", "portfolio.ledger_entries", "broker_secrets", "bars_1m", "depth_snapshots", "live_quotes"];
        foreach (var (name, definition) in definitions)
        {
            foreach (var word in forbidden)
            {
                definition.ShouldNotContain(word, Case.Insensitive, $"aiv.{name}");
            }
        }

        // The C2 predicate is the first thing every portfolio view checks, and only those views carry it.
        foreach (var view in DataManifest.Views)
        {
            var hasPredicate = definitions[view.Name].Contains("context_allows('portfolio'", StringComparison.Ordinal);
            hasPredicate.ShouldBe(view.Class == ViewClass.C2, view.Name);
        }
    }

    [Fact]
    public async Task Every_portfolio_view_is_a_security_barrier_and_no_other_view_needs_to_be()
    {
        await using var connection = new NpgsqlConnection(db.Owner);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT c.relname, coalesce('security_barrier=true' = ANY (c.reloptions), false) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'aiv' AND c.relkind = 'v'", connection);
        var barrier = new Dictionary<string, bool>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                barrier[reader.GetString(0)] = reader.GetBoolean(1);
            }
        }

        foreach (var view in DataManifest.Views)
        {
            barrier[view.Name].ShouldBe(view.Class == ViewClass.C2, view.Name);
        }
    }

    [Fact]
    public async Task A_portfolio_view_shows_the_account_label_never_the_account_id_or_the_client_id()
    {
        await ReadOnlyDatabase.ExecuteAsync(db.Owner, """
            TRUNCATE ai.settings CASCADE;
            DELETE FROM portfolio.holdings;
            DELETE FROM accounts.broker_accounts WHERE slug = 'label-probe';
            INSERT INTO accounts.broker_accounts (id, slug, name, broker, client_id, color, created_at, updated_at, data_enabled, data_primary, data_standby, live_feed_budget)
                VALUES ('00000000-0000-7000-8000-0000000000a1', 'label-probe', 'Label Probe', 'dhan', '1100000001', '#000', now(), now(), false, false, false, 0);
            SET session_replication_role = replica;
            INSERT INTO portfolio.holdings (id, account_id, bucket_id, exchange, security_id, trading_symbol, name, qty, avg_cost, last_price, status, first_seen_at, synced_at)
                VALUES ('00000000-0000-7000-8000-0000000000b1', '00000000-0000-7000-8000-0000000000a1', '00000000-0000-7000-8000-0000000000c1', 'NSE', 'SEC-777', 'PROBE', 'Probe Ltd', 10, 100.5000, 101.0000, 'open', now(), now());
            """);
        try
        {
            await ReadOnlyDatabase.ExecuteAsync(db.App, "INSERT INTO ai.settings (enabled, portfolio_context, max_tool_calls, changed_by, reason) VALUES (true, true, 6, 'owner:probe@example.test', 'label test')");

            await using var connection = new NpgsqlConnection(db.AsSql);
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand("SELECT * FROM aiv.holdings", connection);
            await using var reader = await command.ExecuteReaderAsync(Ct);
            (await reader.ReadAsync(Ct)).ShouldBeTrue();
            var values = Enumerable.Range(0, reader.FieldCount).ToDictionary(reader.GetName, i => reader.GetValue(i)?.ToString());
            values["account"].ShouldBe("Label Probe");
            values["symbol"].ShouldBe("PROBE");
            string.Join('|', values.Values).ShouldNotContain("1100000001");
            string.Join('|', values.Values).ShouldNotContain("SEC-777");
            string.Join('|', values.Keys).ShouldNotContain("account_id");
        }
        finally
        {
            await ReadOnlyDatabase.ExecuteAsync(db.Owner, "TRUNCATE ai.settings CASCADE; DELETE FROM portfolio.holdings; DELETE FROM accounts.broker_accounts WHERE slug = 'label-probe'");
        }
    }
}
