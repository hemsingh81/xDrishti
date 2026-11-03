using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using XDrishti.Api.IntegrationTests.Data;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Api.IntegrationTests.Learning;

/// <summary>
/// P14-06 criteria 2, 4 and 7 on a real database (ADR 0193): <c>AddExpressionProfiles</c> creates the table and widens the hypothesis key column (and goes back, narrowing only when no key is longer than 400), stored six-part keys read back as cash and
/// write back byte for byte, an eight-part key of 64-character parts round-trips through <c>research.cell_stats</c> and <c>research.hypotheses</c>, and a stored profile is never updated.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class ExpressionDimensionTests(ApiFactory factory) : DataTestBase(factory)
{
    private const string Before = "20261102234713_AddFilingExtractions";
    private const string Migration = "20261103044221_AddExpressionProfiles";

    private static readonly string Part = new('a', 64);

    private static readonly string LongKey = string.Join('|', Part, Part, Part, Part, Part, Part, "*", "credit_spread");

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

    private static async Task ExecAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static string InsertHypothesis(string cellKey) =>
        $"INSERT INTO research.hypotheses (state, from_state, hypothesis_id, source, cell_key, knob_name, knob_kind, baseline_value, candidate_value, rationale, baseline_run_key, reason, actor, created_at) VALUES ('proposed', NULL, NULL, 'owner', '{cellKey}', 'slippage', 'Slippage', '1', '2', 'r', '{new string('a', 64)}', 'x', 'cli:xd', now())";

    private static CellStatsRow Row(CellId cell) =>
        new(cell, CellWindow.All, new DateOnly(2026, 10, 9), true, 40, 20, 40, 0.5, 0.5, 0.55, 0.5, 0.6, 2, -1, 0.5, 1.5, 3, 0.3, 0.8, 0.5, 1.5, true);

    [Fact]
    public async Task The_migration_creates_the_table_widens_the_column_agrees_with_the_model_and_down_narrows_only_when_no_key_is_longer_than_400()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(Factory, "xd_expression_probe", Ct, migrate: false);
        using var scope = scratch.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<XDrishtiDbContext>();
        var migrator = db.GetService<IMigrator>();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT tablename FROM pg_tables WHERE schemaname = 'lab' AND tablename = 'expression_profiles'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT character_maximum_length::text FROM information_schema.columns WHERE table_schema = 'research' AND table_name = 'hypotheses' AND column_name = 'cell_key'")).ShouldBe(["400"]);

        await migrator.MigrateAsync(Migration, Ct);

        (await ListAsync(connection, "SELECT column_name || '|' || data_type || '|' || is_nullable FROM information_schema.columns WHERE table_schema = 'lab' AND table_name = 'expression_profiles' ORDER BY ordinal_position")).ShouldBe(
            ["profile_hash|character|NO", "kind|character varying|NO", "params|jsonb|NO", "canonical_text|text|NO", "created_at|timestamp with time zone|NO"]);
        (await ListAsync(connection, "SELECT conname FROM pg_constraint WHERE conrelid = 'lab.expression_profiles'::regclass AND contype <> 'n' ORDER BY 1")).ShouldBe(
            ["ck_expression_profiles_hash", "ck_expression_profiles_kind", "ck_expression_profiles_params", "pk_expression_profiles"]);
        (await ListAsync(connection, "SELECT character_maximum_length::text FROM information_schema.columns WHERE table_schema = 'lab' AND table_name = 'expression_profiles' AND column_name = 'profile_hash'")).ShouldBe(["64"]);
        (await ListAsync(connection, "SELECT character_maximum_length::text FROM information_schema.columns WHERE table_schema = 'research' AND table_name = 'hypotheses' AND column_name = 'cell_key'")).ShouldBe(["600"]);
        (await ListAsync(connection, "SELECT pg_get_expr(adbin, adrelid) FROM pg_attrdef WHERE adrelid = 'lab.expression_profiles'::regclass")).ShouldBe(["now()"]);
        db.Database.HasPendingModelChanges().ShouldBeFalse("the migration and the model snapshot agree");
        await Should.ThrowAsync<PostgresException>(async () => await ExecAsync(connection, "INSERT INTO lab.expression_profiles (profile_hash, kind, params, canonical_text) VALUES ('" + new string('a', 64) + "', 'option_sell', '{}', 'x')"));

        // Down with a key longer than 400 stored: the table goes, the column is kept wide (the log is insert-only; a key is never cut).
        await ExecAsync(connection, "SET session_replication_role = replica"); // the foreign keys of the log to runs are not the subject here
        await ExecAsync(connection, InsertHypothesis(LongKey));
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT tablename FROM pg_tables WHERE schemaname = 'lab' AND tablename = 'expression_profiles'")).ShouldBeEmpty();
        (await ListAsync(connection, "SELECT character_maximum_length::text FROM information_schema.columns WHERE table_schema = 'research' AND table_name = 'hypotheses' AND column_name = 'cell_key'")).ShouldBe(["600"]);
        (await ListAsync(connection, "SELECT cell_key FROM research.hypotheses")).ShouldBe([LongKey]);

        // With no key longer than 400 the same Down narrows it again.
        await migrator.MigrateAsync(Migration, Ct);
        await ExecAsync(connection, "DELETE FROM research.hypotheses");
        await migrator.MigrateAsync(Before, Ct);
        (await ListAsync(connection, "SELECT character_maximum_length::text FROM information_schema.columns WHERE table_schema = 'research' AND table_name = 'hypotheses' AND column_name = 'cell_key'")).ShouldBe(["400"]);
    }

    [Fact]
    public async Task Every_stored_six_part_key_reads_back_as_cash_and_writes_back_byte_identical_and_an_eight_part_key_of_64_character_parts_round_trips()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(Factory, "xd_expression_keys_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICellStatsStore>();
        string[] golden =
        [
            "ORB|15m|BUY|intraday|FIXED_RR|*", "ORB|15m|SELL|swing|FIXED_RR|*", "VWAP_PULLBACK|5m|BUY|intraday|ATR_TRAIL_2.0|*", "ORB|15m|BUY|intraday|FIXED_RR|trend_up_calm", "S1|1d|BUY|swing|RR_2.5|*",
        ];
        var long64 = CellId.TryParse(LongKey).ShouldNotBeNull();
        var option = CellId.TryParse("ORB|15m|BUY|intraday|FIXED_RR|*|*|option_buy").ShouldNotBeNull();
        var rows = golden.Select(k => Row(CellId.TryParse(k)!)).Append(Row(option)).Append(Row(long64)).ToList();

        (await store.StoreAsync(new CellSnapshotToStore(new string('c', 64), new DateOnly(2026, 10, 9), new CellStatsOptions(), "sources", rows, "test", DateTimeOffset.UnixEpoch), Ct)).ShouldBeTrue();

        foreach (var key in golden)
        {
            var read = (await store.FindLatestAsync(key, Ct)).ShouldHaveSingleItem().Row.Cell;
            read.Key.ShouldBe(key);
            (read.Expression, read.Universe, read.IsCash).ShouldBe((Expression.Cash, "*", true));
        }

        var optionRead = (await store.FindLatestAsync(option.Key, Ct)).ShouldHaveSingleItem().Row.Cell;
        (optionRead.Key, optionRead.Expression, optionRead.IsCash).ShouldBe((option.Key, Expression.OptionBuy, false));
        var longRead = (await store.FindLatestAsync(LongKey, Ct)).ShouldHaveSingleItem().Row.Cell;
        (longRead.Key, longRead.Expression).ShouldBe((LongKey, Expression.CreditSpread));
        LongKey.Length.ShouldBeGreaterThan(400);

        // The six-part cash cell and the option cell of one setup are two rows, and the list shows both (the decision paths filter, the lists do not).
        (await store.ListLatestAsync("ORB", "all", Ct)).Select(r => r.Row.Cell.Key).ShouldContain(option.Key);

        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        await ExecAsync(connection, "SET session_replication_role = replica");
        await ExecAsync(connection, InsertHypothesis(LongKey));
        await ExecAsync(connection, InsertHypothesis(golden[0]));
        (await ListAsync(connection, "SELECT cell_key FROM research.hypotheses ORDER BY id")).ShouldBe([LongKey, golden[0]]);
    }

    [Fact]
    public async Task A_stored_row_whose_longer_key_does_not_parse_or_disagrees_with_its_columns_is_an_error_never_a_cash_cell()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(Factory, "xd_expression_corrupt_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICellStatsStore>();
        var cash = CellId.TryParse("ORB|15m|BUY|intraday|FIXED_RR|*")!;
        (await store.StoreAsync(new CellSnapshotToStore(new string('d', 64), new DateOnly(2026, 10, 9), new CellStatsOptions(), "sources", [Row(cash)], "test", DateTimeOffset.UnixEpoch), Ct)).ShouldBeTrue();
        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);

        foreach (var bad in new[] { "ORB|15m|BUY|intraday|FIXED_RR|*|bogus", "ORB|15m|BUY|intraday|FIXED_RR|*|*|option_sell", "XYZ|15m|BUY|intraday|FIXED_RR|*|*|option_buy", "ORB|15m|BUY|intraday|FIXED_RR|*|*|cash" })
        {
            await ExecAsync(connection, $"UPDATE research.cell_stats SET cell_key = '{bad}'");

            await Should.ThrowAsync<InvalidDataException>(async () => await store.FindLatestAsync(bad, Ct), bad);
        }
    }

    [Fact]
    public async Task A_later_snapshot_of_option_cells_alone_does_not_hide_the_cash_snapshot_from_the_decision_reads()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(Factory, "xd_expression_snapshot_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICellStatsStore>();
        var cash = CellId.TryParse("ORB|15m|BUY|intraday|FIXED_RR|*")!;
        var option = CellId.TryParse("ORB|15m|BUY|intraday|FIXED_RR|*|*|option_buy")!;
        var before = new DateOnly(2026, 10, 12);
        (await store.StoreAsync(new CellSnapshotToStore(new string('1', 64), new DateOnly(2026, 10, 8), new CellStatsOptions(), "sources", [Row(cash)], "test", DateTimeOffset.UnixEpoch), Ct)).ShouldBeTrue();
        var baseline = await store.ListLatestBeforeAsync(before, null, "all", Ct);
        var baselineCash = await store.ListLatestCashAsync(null, null, Ct);
        var baselineAsOf = await store.NewestAsOfAsync(Ct);

        (await store.StoreAsync(new CellSnapshotToStore(new string('2', 64), new DateOnly(2026, 10, 9), new CellStatsOptions(), "sources", [Row(option)], "test", DateTimeOffset.UnixEpoch), Ct)).ShouldBeTrue();

        baseline.ShouldHaveSingleItem().Row.Cell.Key.ShouldBe(cash.Key, "the comparison is not vacuous");
        (await store.ListLatestBeforeAsync(before, null, "all", Ct)).Select(r => (r.SnapshotKey, r.Row.Cell.Key)).ShouldBe(baseline.Select(r => (r.SnapshotKey, r.Row.Cell.Key)));
        (await store.ListLatestCashAsync(null, null, Ct)).Select(r => (r.SnapshotKey, r.Row.Cell.Key)).ShouldBe(baselineCash.Select(r => (r.SnapshotKey, r.Row.Cell.Key)));
        (await store.NewestAsOfAsync(Ct)).ShouldBe(baselineAsOf);
        (await store.ListLatestAsync(null, null, Ct)).ShouldHaveSingleItem().Row.Cell.Key.ShouldBe(option.Key, "the display read still shows the newest snapshot");
    }

    [Fact]
    public async Task A_stored_profile_is_content_addressed_read_back_whole_and_never_updated()
    {
        await using var scratch = await ScratchDatabase.CreateAsync(Factory, "xd_expression_profile_probe", Ct);
        using var scope = scratch.Host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IExpressionProfileStore>();
        var profile = ExpressionProfile.Create(Expression.OptionBuy, [new("strike_rule", "atm"), new("premium_stop", 0.5m), new("min_open_interest", 1000)]).Value;
        var same = ExpressionProfile.Create(Expression.OptionBuy, [new("min_open_interest", 1000), new("strike_rule", "atm"), new("premium_stop", 0.50m)]).Value;

        (await store.AppendAsync(profile, Ct)).ShouldBeTrue();
        (await store.AppendAsync(same, Ct)).ShouldBeFalse("the same hash writes nothing");
        (await store.AppendAsync(ExpressionProfile.Cash, Ct)).ShouldBeTrue();

        var read = (await store.GetAsync(profile.Hash, Ct)).ShouldNotBeNull();
        (read.Profile.Hash, read.Profile.CanonicalText, read.Profile.Kind).ShouldBe((profile.Hash, profile.CanonicalText, Expression.OptionBuy));
        (await store.GetAsync(new string('0', 64), Ct)).ShouldBeNull();
        (await store.ListAsync(Ct)).Select(p => p.Profile.Hash).ShouldBe([.. new[] { profile.Hash, ExpressionProfile.Cash.Hash }.Order(StringComparer.Ordinal)], ignoreOrder: true);

        await using var connection = new NpgsqlConnection(scratch.ConnectionString);
        await connection.OpenAsync(Ct);
        (await ListAsync(connection, "SELECT count(*)::text FROM lab.expression_profiles")).ShouldBe(["2"]);

        // A row that does not hash to its key is an error on read, not a profile.
        await ExecAsync(connection, $"UPDATE lab.expression_profiles SET canonical_text = 'tampered' WHERE profile_hash = '{profile.Hash}'");
        await Should.ThrowAsync<InvalidDataException>(async () => await store.GetAsync(profile.Hash, Ct));
    }
}
