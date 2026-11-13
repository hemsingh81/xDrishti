using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// AM-01b (ADR 0282, spec GAP-09): <c>paper.evidence_sets</c>, the frozen evidence set of each edge epoch, insert-only for the runtime role, with a trigger that refuses a row once a shadow entry of the epoch exists; the trial kind <c>evidence_set</c>; the sets of epochs that
    /// already hold shadow entries are written from those entries (so no existing count changes); and the seed row of <c>paper.shadow_settings</c> (actor <c>system:default</c>, ON, reason <c>ADR 0282</c>), written once into an empty table and sealed by the stamp trigger
    /// (refused from the runtime role and into a non-empty table), so the owner's own rows, either direction, always win and a lost table still reads OFF.
    /// </summary>
    public partial class AddEvidenceSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.DropCheckConstraint(
                name: "ck_shadow_settings_shape",
                schema: "paper",
                table: "shadow_settings");

            migrationBuilder.CreateTable(
                name: "evidence_sets",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    edge_epoch = table.Column<long>(type: "bigint", nullable: false),
                    versions = table.Column<string>(type: "jsonb", nullable: false),
                    set_hash = table.Column<string>(type: "char(64)", nullable: false),
                    mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    first_write_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_sets", x => x.id);
                    table.CheckConstraint("ck_evidence_sets_shape", "edge_epoch >= 0 AND jsonb_typeof(versions) = 'array' AND jsonb_array_length(versions) BETWEEN 1 AND 500 AND set_hash ~ '^[0-9a-f]{64}$' AND ((mode = 'auto' AND actor = 'system:default' AND reason IS NULL) OR (mode = 'owner' AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND reason IS NOT NULL AND length(btrim(reason)) > 0) OR (mode = 'backfill' AND actor = 'system:migration' AND reason = 'AM-01b backfill: every version written before evidence sets existed; not pre-registered'))");
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan','null','training_sweep','system_replay','cells_refresh','evidence_set') AND extra_trials >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shadow_settings_shape",
                schema: "paper",
                table: "shadow_settings",
                sql: "(actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0) OR (actor = 'system:default' AND enabled AND reason = 'ADR 0282')");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_sets_edge_epoch_id",
                schema: "paper",
                table: "evidence_sets",
                columns: new[] { "edge_epoch", "id" });

            // Epochs that already hold shadow entries keep counting what they hold, but the set is labelled for what it is: a backfill of every version written before evidence sets existed, NOT a pre-registration (the versions were not chosen before their results).
            // Mode 'backfill', actor 'system:migration', first_write_at NULL, and the versions are registered in the ledger like any set (done before the freezing trigger exists).
            migrationBuilder.Sql("""
                INSERT INTO paper.evidence_sets (edge_epoch, versions, set_hash, mode, actor, reason, first_write_at)
                SELECT k, to_jsonb(array_agg(h ORDER BY h)), encode(sha256(convert_to(string_agg(h, E'\n' ORDER BY h), 'UTF8')), 'hex'), 'backfill', 'system:migration',
                       'AM-01b backfill: every version written before evidence sets existed; not pre-registered', NULL
                FROM (SELECT COALESCE(edge_epoch, 0) AS k, strategy_hash::text AS h FROM paper.shadow_entries GROUP BY 1, 2) x
                GROUP BY k
                """);
            migrationBuilder.Sql("""
                INSERT INTO lab.test_ledger (strategy_id, version, strategy_hash, parent_hash, lab_run_id, run_key, universe_label, window_from, window_to, method, created_at, trial_kind, extra_trials)
                SELECT v.strategy_id, v.version, v.hash, v.parent_hash, md5(s.edge_epoch::text || ':' || s.set_hash || v.hash::text)::uuid, encode(sha256(convert_to(s.edge_epoch::text || ':' || s.set_hash || v.hash::text, 'UTF8')), 'hex'),
                       'evidence_set', (now() AT TIME ZONE 'Asia/Kolkata')::date, (now() AT TIME ZONE 'Asia/Kolkata')::date, 'evidence_set', now(), 'evidence_set', 0
                FROM paper.evidence_sets s CROSS JOIN LATERAL jsonb_array_elements_text(s.versions) AS e(h) JOIN lab.strategy_versions v ON v.hash::text = e.h
                WHERE s.mode = 'backfill'
                ON CONFLICT (lab_run_id) DO NOTHING
                """);

            // The freeze check: an index for the EXISTS, and a guard that is serialised against the first write of the epoch. Writer and owner take the same advisory lock per epoch key (the writer at the start of its transaction, the guard here), so a set and the first entries of an epoch
            // can never both commit unseen by the other under READ COMMITTED. The id is drawn after the lock, so the highest id is the last to commit (the P7-12 pattern).
            migrationBuilder.Sql("CREATE INDEX ix_shadow_entries_edge_epoch ON paper.shadow_entries (edge_epoch)");
            migrationBuilder.Sql("""
                CREATE FUNCTION paper.guard_evidence_set() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('paper.evidence_sets', NEW.edge_epoch));
                    IF NEW.mode = 'backfill' THEN
                        RAISE EXCEPTION 'a backfill set is written only by the migration that introduced evidence sets' USING ERRCODE = '23514';
                    END IF;
                    IF EXISTS (SELECT 1 FROM paper.shadow_entries e WHERE e.edge_epoch = NEW.edge_epoch OR (NEW.edge_epoch = 0 AND e.edge_epoch IS NULL)) THEN
                        RAISE EXCEPTION 'the evidence set of this epoch is frozen: a shadow entry of the epoch exists' USING ERRCODE = '23514';
                    END IF;
                    NEW.id := nextval(pg_get_serial_sequence('paper.evidence_sets', 'id'));
                    NEW.created_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_evidence_sets_guard BEFORE INSERT ON paper.evidence_sets FOR EACH ROW EXECUTE FUNCTION paper.guard_evidence_set()");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        EXECUTE format('GRANT USAGE ON SEQUENCE %s TO xd_app', pg_get_serial_sequence('paper.evidence_sets', 'id'));
                    END IF;
                END $$;
                """);
            migrationBuilder.GrantTableToAppRole(Schemas.Paper, "evidence_sets");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Paper, "evidence_sets");

            // The seed row of the shadow switch, sealed as the kill switch's is (AM-01a): only this migration, as the owner role, into an empty table.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION paper.stamp_shadow_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('paper.shadow_settings', 0));
                    IF NEW.actor = 'system:default' AND (current_user = 'xd_app' OR EXISTS (SELECT 1 FROM paper.shadow_settings)) THEN
                        RAISE EXCEPTION 'the system default row of the shadow switch is written once, by the migration, into an empty table' USING ERRCODE = '23514';
                    END IF;
                    NEW.id := nextval(pg_get_serial_sequence('paper.shadow_settings', 'id'));
                    NEW.changed_at := paper.shadow_now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("""
                INSERT INTO paper.shadow_settings (enabled, actor, reason)
                SELECT true, 'system:default', 'ADR 0282'
                WHERE NOT EXISTS (SELECT 1 FROM paper.shadow_settings)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails closed as the kill switch's does: the seed row goes, the stamp function is the old one, then the table. Ledger rows of kind evidence_set are removed because the old constraint refuses them.
            migrationBuilder.Sql("DELETE FROM paper.shadow_settings WHERE actor = 'system:default' AND reason = 'ADR 0282'");
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION paper.stamp_shadow_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('paper.shadow_settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('paper.shadow_settings', 'id'));
                    NEW.changed_at := paper.shadow_now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("DROP TABLE paper.evidence_sets");
            migrationBuilder.Sql("DROP FUNCTION paper.guard_evidence_set()");
            migrationBuilder.Sql("DROP INDEX paper.ix_shadow_entries_edge_epoch");
            migrationBuilder.Sql("DELETE FROM lab.test_ledger WHERE trial_kind = 'evidence_set'");
            migrationBuilder.DropCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger");

            migrationBuilder.DropCheckConstraint(
                name: "ck_shadow_settings_shape",
                schema: "paper",
                table: "shadow_settings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_test_ledger_trial",
                schema: "lab",
                table: "test_ledger",
                sql: "trial_kind IN ('run','replan','null','training_sweep','system_replay','cells_refresh') AND extra_trials >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_shadow_settings_shape",
                schema: "paper",
                table: "shadow_settings",
                sql: "actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0");
        }
    }
}
