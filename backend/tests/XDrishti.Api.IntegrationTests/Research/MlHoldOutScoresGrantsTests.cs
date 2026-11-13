using System.Globalization;
using Npgsql;
using XDrishti.Api.IntegrationTests.Infrastructure;

namespace XDrishti.Api.IntegrationTests.Research;

/// <summary>
/// CF-10e part 3 (ADR 0285, ADR 0282), migration <c>AddMlHoldOutScores</c> on the dedicated server of <see cref="ResearchGrantsTests"/>: the seed row of <c>ml.holdout.auto_score</c> is written once by the migration and never by the runtime role, an owner can switch the name off and on again,
/// <c>ml.holdout.auto_score</c> accepts nothing but those two kinds of row, and <c>research.ml_holdout_scores</c> is insert-only for the runtime role, one row per model and block, and an automatic row needs the newest switch row to be ON.
/// </summary>
[Collection(ApiTestSuite.Name)]
public sealed class MlHoldOutScoresGrantsTests(ResearchGrantsTests.DedicatedServer server) : IClassFixture<ResearchGrantsTests.DedicatedServer>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string Hash = new('a', 64);

    private const string TrainingSet = """
        INSERT INTO research.training_sets (set_id, label_set_id, rule_version, feature_version, basis, sources, required_features, half_life_days, prior_strength, min_trades, credible_level, from_date, to_date, labelled, row_count,
            positives, negatives, unresolved, no_target, dropped_no_features, dropped_unknown, created_at, created_by)
        VALUES (repeat('a', 64), repeat('a', 64), 'tb-1', 'tf-1', 'Gross', 's', 'cell_n', 180, 20, 30, 0.9, '2026-01-01', '2026-06-01', 10, 10, 6, 4, 0, 0, 0, 0, now(), 'test')
        ON CONFLICT DO NOTHING
        """;

    private const string Model = """
        INSERT INTO research.model_versions (version_key, sha256, artifact_bytes, trainer, note, training_set_id, feature_version, feature_width, fit_rows, calibration_rows, train_rows, from_date, to_date, hold_out_start, verdict, used_folds,
            folds_beating_both, folds_needed, pooled_beats_both, pooled_ece_pp, pooled_log_loss, pooled_brier, reasons, report_text, parameters, calibration_scores, calibration_values, importance, trials, purged_rows, created_at, created_by)
        VALUES (repeat('7', 64), repeat('7', 64), 100, 'FastTree', NULL, repeat('a', 64), 'tf-1', 3, 30, 10, 40, '2026-01-01', '2026-04-01', '2026-05-01', 'Eligible', 6, 5, 4, true, 3.5, 0.65, 0.23, '', 'r', 'p',
            ARRAY[-1.0, 0.0, 1.0], ARRAY[0.3, 0.5, 0.7], ARRAY[0.25, 0.25, 0.25], 1, 4, now(), 'test') RETURNING id
        """;

    private static string Score(long model, string block, string mode = "owner", string actor = "owner:me@x.test") =>
        string.Create(CultureInfo.InvariantCulture, $"INSERT INTO research.ml_holdout_scores (model_id, block_start, look_key, row_count, day_count, logloss_model, logloss_baseline, baseline_name, p_better, low, high, policy_effect_r, input_hash, setting_hash, mode, actor) VALUES ({model}, '{block}', 'model-family:tf-1@{block}', 140, 30, 0.652, 0.671, 'cell_rate', 0.97, 0.001, 0.03, 0.04, '{Hash}', '{Hash}', '{mode}', '{actor}')");

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

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(Ct), CultureInfo.InvariantCulture);
    }

    private static Task SwitchAsync(bool on, string connectionString) =>
        StoreKit.ExecuteAsync(connectionString, $"INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.holdout.auto_score', {on.ToString().ToLowerInvariant()}, 'owner:me@x.test', 'why')", Ct);

    [Fact]
    public async Task The_migration_seeds_one_system_default_row_ON_that_nobody_can_write_again()
    {
        (await ScalarAsync(server.Owner, "SELECT count(*) FROM research.ml_switch_settings WHERE name = 'ml.holdout.auto_score' AND actor = 'system:default' AND reason = 'ADR 0282' AND enabled")).ShouldBe(1);
        (await SqlStateAsync(server.App, "INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.holdout.auto_score', true, 'system:default', 'ADR 0282')")).ShouldBe(PostgresErrorCodes.CheckViolation);
        (await SqlStateAsync(server.Owner, "INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.holdout.auto_score', true, 'system:default', 'ADR 0282')")).ShouldBe(PostgresErrorCodes.CheckViolation, "written once, into a name without rows");
        (await SqlStateAsync(server.Owner, "INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.influence.enabled', true, 'system:default', 'ADR 0282')")).ShouldNotBeNull("only the auto-score name may carry the system row");
    }

    [Fact]
    public async Task An_owner_can_switch_the_name_off_and_on_again_and_the_newest_row_wins()
    {
        await SwitchAsync(false, server.App);
        await SwitchAsync(true, server.App);

        (await ScalarAsync(server.App, "SELECT CASE WHEN enabled THEN 1 ELSE 0 END FROM research.ml_switch_settings WHERE name = 'ml.holdout.auto_score' ORDER BY id DESC LIMIT 1")).ShouldBe(1);
        (await SqlStateAsync(server.App, "INSERT INTO research.ml_switch_settings (name, enabled, actor, reason) VALUES ('ml.holdout.auto_score', false, 'auto', 'why')")).ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task The_scores_are_insert_only_one_per_model_and_block_and_an_automatic_row_needs_the_switch_ON()
    {
        await StoreKit.ExecuteAsync(server.Owner, TrainingSet, Ct);
        var model = await ScalarAsync(server.Owner, Model);

        (await SqlStateAsync(server.App, Score(model, "2026-10-31"))).ShouldBeNull();
        (await SqlStateAsync(server.App, Score(model, "2026-10-31"))).ShouldBe(PostgresErrorCodes.UniqueViolation, "one row per model and block");
        (await SqlStateAsync(server.App, Score(model + 9999, "2026-11-01"))).ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
        (await SqlStateAsync(server.App, Score(model, "2027-05-01", "owner", "auto"))).ShouldBe(PostgresErrorCodes.CheckViolation, "an owner row names an owner");
        foreach (var statement in new[] { "UPDATE research.ml_holdout_scores SET row_count = 1", "DELETE FROM research.ml_holdout_scores", "TRUNCATE research.ml_holdout_scores" })
        {
            (await SqlStateAsync(server.App, statement)).ShouldBe(PostgresErrorCodes.InsufficientPrivilege, statement);
        }

        await SwitchAsync(false, server.App);
        (await SqlStateAsync(server.App, Score(model, "2027-05-01", "auto", "system:auto-score"))).ShouldBe(PostgresErrorCodes.CheckViolation, "the newest switch row is OFF");
        await SwitchAsync(true, server.App);
        (await SqlStateAsync(server.App, Score(model, "2027-05-01", "auto", "system:auto-score"))).ShouldBeNull();
    }
}
