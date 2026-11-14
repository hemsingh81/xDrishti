using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// ADR 0292: the pre-registered system replay over a hold-out block (SR-03, ADR 0284) is dropped with its three tables and the guard of its switches. Raw SQL: no EF entity maps these tables, so the model snapshot does not change.
    /// <c>Down</c> recreates the three tables empty in their last shape (AddReplayHoldOutRegistrations, without its seed rows) with their grants, so the migration chain stays reversible (that migration's <c>Down</c> reads the registrations table).
    /// </summary>
    public partial class DropReplayHoldOutRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.replay_holdout_results");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.replay_holdout_settings");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS research.guard_replay_holdout_setting()");
            migrationBuilder.Sql("DROP TABLE IF EXISTS research.replay_holdout_registrations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE research.replay_holdout_registrations (
                    id uuid NOT NULL,
                    block_start date NOT NULL,
                    config jsonb NOT NULL,
                    config_hash character(64) NOT NULL,
                    mode text NOT NULL,
                    actor text NOT NULL,
                    created_at timestamptz NOT NULL,
                    CONSTRAINT pk_replay_holdout_registrations PRIMARY KEY (id),
                    CONSTRAINT ck_replay_holdout_registrations_shape CHECK (mode IN ('owner', 'auto') AND config_hash ~ '^[0-9a-f]{64}$' AND length(actor) > 0)
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_replay_holdout_registrations_block ON research.replay_holdout_registrations (block_start, created_at DESC)");
            migrationBuilder.Sql("""
                CREATE TABLE research.replay_holdout_results (
                    registration_id uuid NOT NULL,
                    unlock_id uuid NOT NULL,
                    run_ids uuid[] NOT NULL,
                    verdict text NOT NULL,
                    figures jsonb NOT NULL,
                    engine_versions text NOT NULL,
                    mode text NOT NULL,
                    actor text NOT NULL,
                    at timestamptz NOT NULL,
                    CONSTRAINT pk_replay_holdout_results PRIMARY KEY (registration_id),
                    CONSTRAINT fk_replay_holdout_results_registration FOREIGN KEY (registration_id) REFERENCES research.replay_holdout_registrations (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_replay_holdout_results_shape CHECK (verdict IN ('pass', 'fail', 'inconclusive', 'run_failed') AND mode IN ('owner', 'auto'))
                )
                """);
            migrationBuilder.Sql("""
                CREATE TABLE research.replay_holdout_settings (
                    id uuid NOT NULL DEFAULT gen_random_uuid(),
                    name text NOT NULL,
                    enabled boolean NOT NULL,
                    actor text NOT NULL,
                    reason text NOT NULL,
                    at timestamptz NOT NULL,
                    CONSTRAINT pk_replay_holdout_settings PRIMARY KEY (id),
                    CONSTRAINT ck_replay_holdout_settings_shape CHECK (
                        name IN ('replay.holdout.auto_register', 'replay.holdout.auto_run') AND length(btrim(actor)) > 0 AND length(btrim(reason)) > 0
                        AND ((actor LIKE 'owner:%' AND length(btrim(actor)) > 6) OR (actor = 'system:default' AND enabled AND reason = 'ADR 0282')))
                )
                """);

            // as AM-01a's kill-switch seed: the system default row of a switch is written once, by this migration (the owner role), and never by the runtime role, so the runtime can never turn an automatic step back on without the owner's password
            migrationBuilder.Sql("""
                CREATE FUNCTION research.guard_replay_holdout_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW.actor = 'system:default' AND (current_user = 'xd_app' OR EXISTS (SELECT 1 FROM research.replay_holdout_settings WHERE name = NEW.name)) THEN
                        RAISE EXCEPTION 'the system default row of a hold-out replay switch is written once, by the migration, for a switch with no row' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_replay_holdout_settings_guard BEFORE INSERT ON research.replay_holdout_settings FOR EACH ROW EXECUTE FUNCTION research.guard_replay_holdout_setting()");

            foreach (var table in new[] { "replay_holdout_registrations", "replay_holdout_results", "replay_holdout_settings" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Research, table);
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Research, table);
            }
        }
    }
}
