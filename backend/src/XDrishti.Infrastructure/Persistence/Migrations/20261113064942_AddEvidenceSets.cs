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
                    mode = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    first_write_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidence_sets", x => x.id);
                    table.CheckConstraint("ck_evidence_sets_shape", "edge_epoch >= 0 AND jsonb_typeof(versions) = 'array' AND jsonb_array_length(versions) BETWEEN 1 AND 500 AND set_hash ~ '^[0-9a-f]{64}$' AND ((mode = 'auto' AND actor = 'system:default' AND reason IS NULL) OR (mode = 'owner' AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND reason IS NOT NULL AND length(btrim(reason)) > 0))");
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

            // Epochs that already hold shadow entries keep counting what they hold: their set is the distinct versions of their entries (done before the freezing trigger exists).
            migrationBuilder.Sql("""
                INSERT INTO paper.evidence_sets (edge_epoch, versions, set_hash, mode, actor, reason, first_write_at)
                SELECT k, to_jsonb(array_agg(h ORDER BY h)), encode(sha256(convert_to(string_agg(h, E'\n' ORDER BY h), 'UTF8')), 'hex'), 'auto', 'system:default', NULL, min(first_at)
                FROM (SELECT COALESCE(edge_epoch, 0) AS k, strategy_hash::text AS h, min(written_at) AS first_at FROM paper.shadow_entries GROUP BY 1, 2) x
                GROUP BY k
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION paper.guard_evidence_set() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF EXISTS (SELECT 1 FROM paper.shadow_entries e WHERE e.edge_epoch = NEW.edge_epoch OR (NEW.edge_epoch = 0 AND e.edge_epoch IS NULL)) THEN
                        RAISE EXCEPTION 'the evidence set of this epoch is frozen: a shadow entry of the epoch exists' USING ERRCODE = '23514';
                    END IF;
                    NEW.created_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_evidence_sets_guard BEFORE INSERT ON paper.evidence_sets FOR EACH ROW EXECUTE FUNCTION paper.guard_evidence_set()");
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
