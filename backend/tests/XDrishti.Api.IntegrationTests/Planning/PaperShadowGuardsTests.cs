using System.Globalization;
using Npgsql;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Shadow;
using XDrishti.Domain.Shadow;
using XDrishti.Infrastructure.Learning;
using XDrishti.Infrastructure.Paper;
using XDrishti.Api.IntegrationTests.Infrastructure;
using XDrishti.Api.IntegrationTests.Research;

namespace XDrishti.Api.IntegrationTests.Planning;

/// <summary>
/// CF-08 part A (ADR 0211), criteria 2 and 4, on the dedicated throw-away server where the runtime role <c>xd_app</c> exists with the init script's default privileges. The database clock is one function, <c>paper.shadow_now()</c>, which the owner role
/// replaces here to fix the time; the runtime role cannot. Every fact inserts as <c>xd_app</c>: the guards must hold for the role that the writer runs as, including a direct insert that names its own <c>written_at</c>.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class PaperShadowGuardsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>, IAsyncLifetime
{
    private static readonly string Hash = new('a', 64);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await StoreKit.ExecuteAsync(server.Owner, "DELETE FROM paper.shadow_scores; DELETE FROM paper.shadow_grades; DELETE FROM paper.shadow_entries; DELETE FROM paper.shadow_sessions; DELETE FROM paper.shadow_settings; DELETE FROM research.ml_monitor_checks", Ct);
        await SetClockAsync(DateTimeOffset.UtcNow);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private Task SetClockAsync(DateTimeOffset at) =>
        StoreKit.ExecuteAsync(server.Owner, $"CREATE OR REPLACE FUNCTION paper.shadow_now() RETURNS timestamptz LANGUAGE sql AS $$ SELECT '{at.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)}+00'::timestamptz $$", Ct);

    private async Task<string?> SqlStateAsync(string sql, params (string Name, object Value)[] parameters)
    {
        try
        {
            await StoreKit.ExecuteAsync(server.App, sql, Ct, parameters);
            return null;
        }
        catch (PostgresException failure)
        {
            return failure.SqlState;
        }
    }

    private static async Task<T> ScalarAsync<T>(string connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, c);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    private async Task<DateTimeOffset> InstantAsync(string sql) => new(DateTime.SpecifyKind(await ScalarAsync<DateTime>(server.Owner, sql), DateTimeKind.Utc));

    /// <summary>A plan for <paramref name="session"/> stored at <paramref name="storedAt"/> with <paramref name="items"/> primary tickets; returns the plan and the item ids. Inserted as the owner (the plan record is not what is under test).</summary>
    private async Task<(long Plan, long[] Items)> PlanAsync(DateOnly session, DateTimeOffset storedAt, int items = 1)
    {
        var plan = await ScalarAsync<long>(
            server.Owner,
            "INSERT INTO trading.plans (plan_key, for_date, review_cutoff, version, input_hash, basis, provisional, notice, limits, created_by, created_at) VALUES (@k, @d, @c, 1, repeat('a', 64), 'b', true, 'n', 'l', 'probe', @at) RETURNING id",
            ("k", "shadow-" + Guid.NewGuid().ToString("N")), ("d", session), ("c", new DateTimeOffset(session.ToDateTime(new TimeOnly(3, 35)), TimeSpan.Zero)), ("at", storedAt.ToUniversalTime()));
        var ids = new List<long>();
        for (var rank = 1; rank <= items; rank++)
        {
            ids.Add(await ScalarAsync<long>(
                server.Owner,
                "INSERT INTO trading.plan_items (plan_id, item_rank, item_group, symbol, side, strategy_id, strategy_version_hash, trigger_text, stop_text, target_text, validity_text, reasons, cell_key, trades, data_range, p_win_basis, unavailable) VALUES (@p, @r, 'primary', 's', 'BUY', 'ORB', 'h', 't', 's', 't', 'v', '{}', 'c', 1, 'd', 'b', '{}') RETURNING id",
                ("p", plan), ("r", rank)));
        }

        return (plan, [.. ids]);
    }

    private Task<string?> SessionAsync(DateOnly date, string status = "ok", string? prev = null, string? digest = null, int rows = 1) =>
        SqlStateAsync(
            "INSERT INTO paper.shadow_sessions (session_date, n_rows, status, digest, prev_digest, written_at) VALUES (@d, @n, @s, @digest, @prev, '2000-01-01')",
            ("d", date), ("n", status == "gap" ? 0 : rows), ("s", status), ("digest", digest ?? Hash), ("prev", (object?)prev ?? DBNull.Value));

    private Task<string?> EntryAsync(DateOnly date, long plan, long item, string? key = null) =>
        SqlStateAsync(
            "INSERT INTO paper.shadow_entries (session_date, plan_id, plan_item_id, trade_key, ticket_group, cell_key, strategy_hash, symbol, side, time_rule, counts_for_edge, written_at, row_hash) VALUES (@d, @p, @i, @k, 'primary', 'c', repeat('a', 64), 's', 'BUY', 'v', true, '2000-01-01', repeat('b', 64))",
            ("d", date), ("p", plan), ("i", item), ("k", key ?? "ORB|s|Buy|" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second = 0, int ms = 0) => new(year, month, day, hour, minute, second, ms, TimeSpan.Zero);

    [Fact]
    public async Task A_shadow_score_is_stamped_by_the_database_refused_from_0900_unique_per_entry_and_model_and_never_changed_or_deleted_by_the_runtime_role()
    {
        // CF-10e part 10e-1 (ADR 0281), acceptance criterion 1
        await StoreKit.ExecuteAsync(
            server.Owner,
            "INSERT INTO research.training_sets (set_id, label_set_id, rule_version, feature_version, basis, sources, required_features, half_life_days, prior_strength, min_trades, credible_level, from_date, to_date, labelled, row_count, positives, negatives, unresolved, no_target, dropped_no_features, dropped_unknown, created_at, created_by) VALUES (repeat('a', 64), repeat('a', 64), 'tb-1', 'tf-1', 'Gross', 's', 'cell_n', 180, 20, 30, 0.9, '2026-01-01', '2026-06-01', 10, 10, 6, 4, 0, 0, 0, 0, now(), 'test') ON CONFLICT DO NOTHING", Ct);
        var model = await ScalarAsync<long>(
            server.Owner,
            "INSERT INTO research.model_versions (version_key, sha256, artifact_bytes, trainer, note, training_set_id, feature_version, feature_width, fit_rows, calibration_rows, train_rows, from_date, to_date, hold_out_start, verdict, used_folds, folds_beating_both, folds_needed, pooled_beats_both, pooled_ece_pp, pooled_log_loss, pooled_brier, reasons, report_text, parameters, calibration_scores, calibration_values, importance, trials, purged_rows, created_at, created_by) VALUES (repeat('c', 64), repeat('c', 64), 100, 'FastTree', NULL, repeat('a', 64), 'tf-1', 3, 30, 10, 40, '2026-01-01', '2026-04-01', '2026-05-01', 'Eligible', 6, 5, 4, true, 3.5, 0.65, 0.23, '', 'r', 'p', ARRAY[-1.0, 0.0, 1.0], ARRAY[0.3, 0.5, 0.7], ARRAY[0.25, 0.25, 0.25], 1, 4, now(), 'test') RETURNING id");
        var session = new DateOnly(2027, 3, 16);
        var (plan, items) = await PlanAsync(session, Utc(2027, 3, 15, 15, 40));
        await SetClockAsync(Utc(2027, 3, 16, 3, 0));
        (await SessionAsync(session)).ShouldBeNull();
        (await EntryAsync(session, plan, items[0])).ShouldBeNull();
        var entry = await ScalarAsync<long>(server.Owner, "SELECT id FROM paper.shadow_entries");
        string Score(double p = 0.6) => $"INSERT INTO paper.shadow_scores (entry_id, model_id, model_sha256, feature_version, p_win, features_hash, scored_at) VALUES ({entry}, {model}, repeat('c', 64), 'tf-1', {p.ToString(CultureInfo.InvariantCulture)}, repeat('d', 64), '2000-01-01')";

        (await SqlStateAsync(Score())).ShouldBeNull();
        (await InstantAsync("SELECT scored_at FROM paper.shadow_scores")).ShouldBe(Utc(2027, 3, 16, 3, 0));
        (await SqlStateAsync(Score())).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await SqlStateAsync(Score(1.5))).ShouldNotBeNull();
        (await SqlStateAsync("UPDATE paper.shadow_scores SET p_win = 0.1")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync("DELETE FROM paper.shadow_scores")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        await StoreKit.ExecuteAsync(server.Owner, "DELETE FROM paper.shadow_scores", Ct);
        await SetClockAsync(Utc(2027, 3, 16, 3, 31)); // 09:01 IST
        (await SqlStateAsync(Score())).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM paper.shadow_scores", Ct)).ShouldBe(0);
    }

    private async Task<long> ModelAsync()
    {
        await StoreKit.ExecuteAsync(
            server.Owner,
            "INSERT INTO research.training_sets (set_id, label_set_id, rule_version, feature_version, basis, sources, required_features, half_life_days, prior_strength, min_trades, credible_level, from_date, to_date, labelled, row_count, positives, negatives, unresolved, no_target, dropped_no_features, dropped_unknown, created_at, created_by) VALUES (repeat('a', 64), repeat('a', 64), 'tb-1', 'tf-1', 'Gross', 's', 'cell_n', 180, 20, 30, 0.9, '2026-01-01', '2026-06-01', 10, 10, 6, 4, 0, 0, 0, 0, now(), 'test') ON CONFLICT DO NOTHING", Ct);
        var key = Guid.NewGuid().ToString("N")[0];
        return await ScalarAsync<long>(
            server.Owner,
            $"INSERT INTO research.model_versions (version_key, sha256, artifact_bytes, trainer, note, training_set_id, feature_version, feature_width, fit_rows, calibration_rows, train_rows, from_date, to_date, hold_out_start, verdict, used_folds, folds_beating_both, folds_needed, pooled_beats_both, pooled_ece_pp, pooled_log_loss, pooled_brier, reasons, report_text, parameters, calibration_scores, calibration_values, importance, trials, purged_rows, created_at, created_by) VALUES (repeat('{key}', 64), repeat('e', 64), 100, 'FastTree', NULL, repeat('a', 64), 'tf-1', 3, 30, 10, 40, '2026-01-01', '2026-04-01', '2026-05-01', 'Eligible', 6, 5, 4, true, 3.5, 0.65, 0.23, '', 'r', 'p', ARRAY[-1.0, 0.0, 1.0], ARRAY[0.3, 0.5, 0.7], ARRAY[0.25, 0.25, 0.25], 1, 4, now(), 'test') RETURNING id");
    }

    private static ShadowEntry Candidate(DateOnly session, long plan, long item, string key) =>
        new(session, plan, item, key, "primary", "c", Hash, "s", "BUY", null, null, null, "v", null, null, null, null, null, true, null);

    [Fact]
    public async Task The_store_writes_the_scores_with_the_session_resolving_each_entry_by_its_trade_key_and_the_reader_returns_only_graded_ones()
    {
        // CF-10e part 10e-1: the store, the reader and the monitor-row reader against the real database
        var model = await ModelAsync();
        var session = new DateOnly(2027, 4, 6);
        var (plan, items) = await PlanAsync(session, Utc(2027, 4, 5, 15, 40), 2);
        await SetClockAsync(Utc(2027, 4, 6, 3, 0));
        await using var source = NpgsqlDataSource.Create(server.App);
        var entries = new[] { Candidate(session, plan, items[0], "k-a"), Candidate(session, plan, items[1], "k-b") };
        var scores = new[] { new NewShadowScore("k-a", model, new string('e', 64), "tf-1", 0.7, new string('d', 64)), new NewShadowScore("k-b", model, new string('e', 64), "tf-1", 0.4, new string('d', 64)) };

        (await new ShadowStore(source).WriteSessionAsync(new NewShadowSession(session, "ok", null, ShadowDigest.Next(null, session, "ok", entries.Select(e => e.RowHash)), entries, scores), Ct)).ShouldBeTrue();

        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM paper.shadow_scores", Ct)).ShouldBe(2);
        (await ScalarAsync<double>(server.Owner, "SELECT s.p_win FROM paper.shadow_scores s JOIN paper.shadow_entries e ON e.id = s.entry_id WHERE e.trade_key = 'k-b'")).ShouldBe(0.4);
        var reader = new ShadowScoreReader(source);
        (await reader.OutcomesAsync(model, Ct)).ShouldBeEmpty("no grade yet");

        await SetClockAsync(Utc(2027, 4, 6, 11, 0));
        await StoreKit.ExecuteAsync(server.Owner, "INSERT INTO paper.shadow_grades (entry_id, status, r_sim, r_basis, r_gross, engine_version) SELECT id, 'graded', 1.5, 'gross', 1.5, 'e1' FROM paper.shadow_entries WHERE trade_key = 'k-a'", Ct);
        await StoreKit.ExecuteAsync(server.Owner, "INSERT INTO paper.shadow_grades (entry_id, status, engine_version) SELECT id, 'not_triggered', 'e1' FROM paper.shadow_entries WHERE trade_key = 'k-b'", Ct);
        var outcomes = await reader.OutcomesAsync(model, Ct);
        outcomes.Count.ShouldBe(1);
        outcomes[0].ShouldBe(new XDrishti.Domain.Learning.ScoredOutcome(session, "k-a", 0.7, 1.5));

        var checks = new MlMonitorChecks(source);
        (await checks.LatestAsync(model, Ct)).ShouldBeNull();
        (await checks.InsertAsync(new NewMlMonitorCheck(model, new string('f', 64), "[{\"Kind\":\"calibration\",\"State\":\"Quiet\"}]"), new DateOnly(2027, 4, 6), Ct)).ShouldBeTrue();
        (await checks.InsertAsync(new NewMlMonitorCheck(model, new string('f', 64), "[{\"Kind\":\"calibration\",\"State\":\"Fires\"}]"), new DateOnly(2027, 4, 7), Ct)).ShouldBeTrue();
        var latest = await checks.LatestAsync(model, Ct);
        latest!.CheckedOn.ShouldBe(new DateOnly(2027, 4, 7));
        latest.Verdicts.ShouldContain("Fires");
    }

    [Fact]
    public async Task The_registry_answers_with_one_existence_query_whether_any_model_was_ever_champion()
    {
        var model = await ModelAsync();
        await using var source = NpgsqlDataSource.Create(server.App);
        var registry = new ModelRegistry(source);

        (await registry.AnyChampionEverAsync(Ct)).ShouldBeFalse();

        await StoreKit.ExecuteAsync(server.Owner, $"INSERT INTO research.model_designations (model_id, role, reason, created_at, created_by) VALUES ({model}, 'champion', 'r', now(), 'test')", Ct);
        await StoreKit.ExecuteAsync(server.Owner, $"INSERT INTO research.model_designations (model_id, role, reason, created_at, created_by) VALUES ({model}, 'retired', 'r', now(), 'test')", Ct);

        (await registry.AnyChampionEverAsync(Ct)).ShouldBeTrue("a retired champion still counts");
    }

    [Fact]
    public async Task A_refused_score_row_rolls_back_to_the_savepoint_and_the_session_and_its_entries_are_still_committed()
    {
        var model = await ModelAsync();
        var session = new DateOnly(2027, 4, 13);
        var (plan, items) = await PlanAsync(session, Utc(2027, 4, 12, 15, 40));
        await SetClockAsync(Utc(2027, 4, 13, 3, 0));
        await using var source = NpgsqlDataSource.Create(server.App);
        var entries = new[] { Candidate(session, plan, items[0], "k-z") };
        var refused = new NewShadowScore("k-z", model, new string('e', 64), "tf-1", 1.5, new string('d', 64)); // p_win outside [0, 1]: the CHECK refuses it

        (await new ShadowStore(source).WriteSessionAsync(new NewShadowSession(session, "ok", null, ShadowDigest.Next(null, session, "ok", entries.Select(e => e.RowHash)), entries, [refused]), Ct)).ShouldBeTrue();

        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM paper.shadow_entries WHERE session_date = '2027-04-13'", Ct)).ShouldBe(1);
        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM paper.shadow_sessions WHERE session_date = '2027-04-13'", Ct)).ShouldBe(1);
        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM paper.shadow_scores", Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Before_0900_India_time_the_session_and_its_row_are_accepted_and_stamped_by_the_database_whatever_the_insert_says()
    {
        var session = new DateOnly(2026, 11, 3);
        var (plan, items) = await PlanAsync(session, Utc(2026, 11, 2, 15, 40)); // stored 2026-11-02 21:10 IST
        await SetClockAsync(Utc(2026, 11, 3, 3, 29)); // 08:59 IST

        (await SessionAsync(session)).ShouldBeNull();
        (await EntryAsync(session, plan, items[0])).ShouldBeNull();

        (await InstantAsync("SELECT written_at FROM paper.shadow_entries")).ShouldBe(Utc(2026, 11, 3, 3, 29));
        (await InstantAsync("SELECT written_at FROM paper.shadow_sessions")).ShouldBe(Utc(2026, 11, 3, 3, 29));
    }

    [Fact]
    public async Task From_0900_India_time_a_direct_insert_is_refused_at_0900_and_at_0901_and_accepted_at_0859()
    {
        var session = new DateOnly(2026, 11, 4);
        var (plan, items) = await PlanAsync(session, Utc(2026, 11, 3, 15, 40), 3);
        await SetClockAsync(Utc(2026, 11, 4, 3, 0));
        (await SessionAsync(session)).ShouldBeNull();

        await SetClockAsync(Utc(2026, 11, 4, 3, 31)); // 09:01 IST
        (await EntryAsync(session, plan, items[0])).ShouldBe(PostgresErrorCodes.CheckViolation);
        await SetClockAsync(Utc(2026, 11, 4, 3, 30)); // 09:00:00 IST exactly: at the cut-off is already late
        (await EntryAsync(session, plan, items[0])).ShouldBe(PostgresErrorCodes.CheckViolation);
        await SetClockAsync(Utc(2026, 11, 4, 3, 29, 59, 999)); // 08:59:59.999 IST
        (await EntryAsync(session, plan, items[1])).ShouldBeNull();

        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM paper.shadow_entries", Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task A_session_that_has_opened_gets_no_ok_session_and_no_row_even_if_the_plan_is_stored()
    {
        var session = new DateOnly(2026, 11, 2);
        var (plan, items) = await PlanAsync(session, Utc(2026, 11, 1, 15, 40));
        await SetClockAsync(Utc(2026, 11, 3, 4, 0)); // the next day

        (await SessionAsync(session)).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await EntryAsync(session, plan, items[0])).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await StoreKit.CountAsync(server.Owner, "SELECT count(*) FROM paper.shadow_sessions", Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_gap_is_only_for_a_session_that_has_opened_and_has_no_rows()
    {
        await SetClockAsync(Utc(2026, 11, 3, 4, 0)); // 09:30 IST on the 3rd

        (await SessionAsync(new DateOnly(2026, 11, 4), "gap")).ShouldBe(PostgresErrorCodes.CheckViolation); // not opened yet
        (await SessionAsync(new DateOnly(2026, 11, 3), "gap")).ShouldBeNull();
        await SetClockAsync(Utc(2026, 11, 4, 4, 0));
        (await SqlStateAsync($"INSERT INTO paper.shadow_sessions (session_date, n_rows, status, digest, prev_digest) VALUES ('2026-11-04', 3, 'gap', repeat('c', 64), '{Hash}')")).ShouldBe(PostgresErrorCodes.CheckViolation); // a gap with rows
        (await SessionAsync(new DateOnly(2026, 11, 4), "gap", prev: Hash, digest: new string('c', 64))).ShouldBeNull();
    }

    [Fact]
    public async Task A_row_older_than_its_plan_a_row_for_another_date_and_a_row_in_a_gap_are_refused()
    {
        var session = new DateOnly(2026, 11, 5);
        var (plan, items) = await PlanAsync(session, Utc(2026, 11, 4, 15, 40), 2);
        await SetClockAsync(Utc(2026, 11, 4, 15, 0)); // before the plan was stored
        (await SessionAsync(session)).ShouldBeNull();
        (await EntryAsync(session, plan, items[0])).ShouldBe(PostgresErrorCodes.CheckViolation);

        await SetClockAsync(Utc(2026, 11, 4, 16, 0));
        (await SessionAsync(new DateOnly(2026, 11, 6), prev: Hash, digest: new string('c', 64))).ShouldBeNull();
        (await EntryAsync(new DateOnly(2026, 11, 6), plan, items[1])).ShouldBe(PostgresErrorCodes.CheckViolation); // the plan is for the 5th
        (await EntryAsync(session, plan, items[0])).ShouldBeNull();
    }

    [Fact]
    public async Task The_chain_only_moves_forward_and_links_to_the_newest_session()
    {
        await SetClockAsync(Utc(2026, 12, 1, 3, 0));
        (await SessionAsync(new DateOnly(2026, 12, 2), prev: Hash)).ShouldBe(PostgresErrorCodes.CheckViolation); // the first session has no previous digest
        (await SessionAsync(new DateOnly(2026, 12, 2))).ShouldBeNull();

        (await SessionAsync(new DateOnly(2026, 12, 3))).ShouldBe(PostgresErrorCodes.CheckViolation); // does not link
        (await SessionAsync(new DateOnly(2026, 12, 3), prev: new string('f', 64))).ShouldBe(PostgresErrorCodes.CheckViolation); // links to something else
        (await SessionAsync(new DateOnly(2026, 12, 1), prev: Hash)).ShouldBe(PostgresErrorCodes.CheckViolation); // earlier than the newest
        (await SessionAsync(new DateOnly(2026, 12, 3), prev: Hash, digest: new string('d', 64))).ShouldBeNull();
        (await SessionAsync(new DateOnly(2026, 12, 3), prev: Hash, digest: new string('e', 64))).ShouldBe(PostgresErrorCodes.CheckViolation); // a second session for the date
    }

    [Fact]
    public async Task One_trade_key_per_session_and_one_row_per_plan_item()
    {
        var session = new DateOnly(2026, 11, 9);
        var (plan, items) = await PlanAsync(session, Utc(2026, 11, 8, 15, 40), 2);
        await SetClockAsync(Utc(2026, 11, 9, 2, 0));
        (await SessionAsync(session, rows: 2)).ShouldBeNull();
        (await EntryAsync(session, plan, items[0], "same")).ShouldBeNull();

        (await EntryAsync(session, plan, items[1], "same")).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await EntryAsync(session, plan, items[0], "other")).ShouldBe(PostgresErrorCodes.UniqueViolation);
        (await EntryAsync(session, plan, items[1], "other")).ShouldBeNull();
    }

    [Fact]
    public async Task The_runtime_role_reads_and_inserts_and_can_neither_update_delete_nor_truncate_the_shadow_tables()
    {
        var session = new DateOnly(2026, 11, 10);
        var (plan, items) = await PlanAsync(session, Utc(2026, 11, 9, 15, 40));
        await SetClockAsync(Utc(2026, 11, 10, 2, 0));
        (await SessionAsync(session)).ShouldBeNull();
        (await EntryAsync(session, plan, items[0])).ShouldBeNull();
        (await SqlStateAsync("INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (true, 'owner:probe@example.test', 'probe')")).ShouldBeNull();

        foreach (var table in new[] { "shadow_entries", "shadow_sessions", "shadow_grades", "shadow_settings" })
        {
            (await SqlStateAsync($"SELECT count(*) FROM paper.{table}")).ShouldBeNull();
            (await SqlStateAsync($"DELETE FROM paper.{table}")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, $"delete from {table}");
            (await SqlStateAsync($"TRUNCATE paper.{table}")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, $"truncate {table}");
        }

        (await SqlStateAsync("UPDATE paper.shadow_entries SET symbol = 'x'")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync("UPDATE paper.shadow_sessions SET n_rows = 0")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync("UPDATE paper.shadow_grades SET r_sim = 1")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        (await SqlStateAsync("UPDATE paper.shadow_settings SET enabled = false")).ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        foreach (var statement in new[]
        {
            "CREATE OR REPLACE FUNCTION paper.shadow_now() RETURNS timestamptz LANGUAGE sql AS $$ SELECT now() $$",
            "ALTER TABLE paper.shadow_entries DISABLE TRIGGER trg_shadow_entries_guard",
            "DROP TRIGGER trg_shadow_sessions_guard ON paper.shadow_sessions",
        })
        {
            (await SqlStateAsync(statement)).ShouldNotBeNull($"the runtime role cannot: {statement}");
        }
    }

    [Fact]
    public async Task The_switch_row_is_stamped_by_the_database_and_must_name_an_owner_and_a_reason()
    {
        await SetClockAsync(Utc(2026, 11, 10, 2, 0));

        (await SqlStateAsync("INSERT INTO paper.shadow_settings (enabled, actor, reason, changed_at) VALUES (true, 'owner:probe@example.test', 'probe', '2000-01-01')")).ShouldBeNull();
        (await SqlStateAsync("INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (true, 'system:writer', 'probe')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync("INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (true, 'owner:probe@example.test', '  ')")).ShouldBe(PostgresErrorCodes.CheckViolation);

        (await InstantAsync("SELECT changed_at FROM paper.shadow_settings")).ShouldBe(Utc(2026, 11, 10, 2, 0));
    }

    [Fact]
    public async Task Two_changes_of_the_switch_are_serialised_so_that_the_highest_id_is_the_last_to_commit()
    {
        const string Insert = "INSERT INTO paper.shadow_settings (enabled, actor, reason) VALUES (@e, 'owner:probe@example.test', 'probe') RETURNING id";
        await using var first = new NpgsqlConnection(server.App);
        await first.OpenAsync(Ct);
        await using var transaction = await first.BeginTransactionAsync(Ct);
        await using var one = new NpgsqlCommand(Insert, first, transaction);
        one.Parameters.AddWithValue("e", true);
        var firstId = (long)(await one.ExecuteScalarAsync(Ct))!;

        var second = SecondAsync();
        await WaitForAdvisoryWaiterAsync(); // the second change waits for the first to end; with the id drawn before the lock it would not wait
        await transaction.CommitAsync(Ct);
        var secondId = await second;

        secondId.ShouldBeGreaterThan(firstId);
        (await ScalarAsync<bool>(server.Owner, "SELECT enabled FROM paper.shadow_settings ORDER BY id DESC LIMIT 1")).ShouldBeFalse();

        async Task<long> SecondAsync()
        {
            await using var connection = new NpgsqlConnection(server.App);
            await connection.OpenAsync(Ct);
            await using var command = new NpgsqlCommand(Insert, connection);
            command.Parameters.AddWithValue("e", false);
            return (long)(await command.ExecuteScalarAsync(Ct))!;
        }
    }

    private async Task WaitForAdvisoryWaiterAsync()
    {
        // Polls the lock table (bounded) until a backend waits for an advisory lock; there is no clock to advance here, the wait is another backend's in the database (the same poll as AutoApprovalDatabaseTests).
        for (var i = 0; i < 200; i++)
        {
            if (await ScalarAsync<long>(server.Owner, "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted") > 0)
            {
                return;
            }

            await Task.Delay(25, Ct);
        }

        throw new InvalidOperationException("Nobody waited for the advisory lock.");
    }

    [Fact]
    public async Task A_grade_is_stamped_by_the_database_once_per_entry_and_never_before_the_open_of_its_session()
    {
        var session = new DateOnly(2026, 11, 11);
        var (plan, items) = await PlanAsync(session, Utc(2026, 11, 10, 15, 40));
        await SetClockAsync(Utc(2026, 11, 11, 2, 0));
        (await SessionAsync(session)).ShouldBeNull();
        (await EntryAsync(session, plan, items[0])).ShouldBeNull();
        var entry = await ScalarAsync<long>(server.Owner, "SELECT id FROM paper.shadow_entries");
        const string Grade = "INSERT INTO paper.shadow_grades (entry_id, status, r_sim, r_basis, r_gross, engine_version, graded_at) VALUES (@e, @s, @r, @b, @r, 'sim-6', '2000-01-01')";

        (await SqlStateAsync(Grade, ("e", entry), ("s", "graded"), ("r", 1.5), ("b", "net"))).ShouldBe(PostgresErrorCodes.CheckViolation); // before the open

        await SetClockAsync(Utc(2026, 11, 11, 11, 0));
        (await SqlStateAsync(Grade, ("e", entry), ("s", "graded"), ("r", DBNull.Value), ("b", "net"))).ShouldBe(PostgresErrorCodes.CheckViolation); // graded needs an R
        (await SqlStateAsync(Grade, ("e", entry), ("s", "graded"), ("r", 1.5), ("b", "other"))).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync("INSERT INTO paper.shadow_grades (entry_id, status, r_sim, r_basis, engine_version, graded_at) VALUES (@e, 'graded', 1.5, 'net', 'sim-6', '2000-01-01')", ("e", entry))).ShouldBe(PostgresErrorCodes.CheckViolation); // graded needs the gross R too (ADR 0232)
        (await SqlStateAsync("INSERT INTO paper.shadow_grades (entry_id, status, r_gross, engine_version, graded_at) VALUES (@e, 'not_triggered', 1.5, 'sim-6', '2000-01-01')", ("e", entry))).ShouldBe(PostgresErrorCodes.CheckViolation); // and only a graded row has one
        (await SqlStateAsync(Grade, ("e", entry), ("s", "graded"), ("r", 1.5), ("b", "net"))).ShouldBeNull();
        (await SqlStateAsync(Grade, ("e", entry), ("s", "graded"), ("r", 2.5), ("b", "net"))).ShouldBe(PostgresErrorCodes.UniqueViolation); // a graded entry is never rewritten

        (await InstantAsync("SELECT graded_at FROM paper.shadow_grades")).ShouldBe(Utc(2026, 11, 11, 11, 0));
    }

    [Fact]
    public async Task The_result_reader_hands_over_the_epoch_a_candidate_was_written_in_and_its_grade_for_the_sequential_test()
    {
        // CF-09: the sequential test reads the current epoch's rows only, so the reader must return the stored edge epoch (null when there was none)
        var session = new DateOnly(2026, 11, 12);
        var (plan, items) = await PlanAsync(session, Utc(2026, 11, 11, 15, 40), 3);
        await SetClockAsync(Utc(2026, 11, 12, 2, 0));
        (await SessionAsync(session, rows: 3)).ShouldBeNull();
        for (var i = 0; i < 3; i++)
        {
            (await EntryAsync(session, plan, items[i], "ORB|s" + i + "|Buy|2026-11-12")).ShouldBeNull();
        }

        await StoreKit.ExecuteAsync(server.Owner, "UPDATE paper.shadow_entries SET edge_epoch = 4 WHERE trade_key <> 'ORB|s2|Buy|2026-11-12'", Ct);
        await SetClockAsync(Utc(2026, 11, 12, 11, 0));
        var first = await ScalarAsync<long>(server.Owner, "SELECT id FROM paper.shadow_entries WHERE trade_key = 'ORB|s0|Buy|2026-11-12'");
        (await SqlStateAsync("INSERT INTO paper.shadow_grades (entry_id, status, r_sim, r_basis, r_gross, engine_version, graded_at) VALUES (@e, 'graded', -1.0, 'gross', -1.0, 'sim-6', '2000-01-01')", ("e", first))).ShouldBeNull();
        await using var source = NpgsqlDataSource.Create(server.App);

        var rows = await new XDrishti.Infrastructure.Paper.ShadowResultReader(source).ResultsAsync(Ct);

        rows.Select(r => (r.TradeKey, r.EdgeEpoch, r.Status)).ShouldBe(
            [("ORB|s0|Buy|2026-11-12", 4L, "graded"), ("ORB|s1|Buy|2026-11-12", 4L, "pending"), ("ORB|s2|Buy|2026-11-12", null, "pending")], ignoreOrder: true);
        rows.Single(r => r.Status == "graded").RSim.ShouldBe(-1.0);
    }
}
