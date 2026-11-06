using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The readiness scorecard (spec CF-11 part 1, ADR 0221): the schema <c>readiness</c> with <c>settings</c> (the owner's enforce switch of the readiness gates, append-only, id in commit order, time by the database clock) and <c>scorecard_snapshots</c> (insert-only daily and
    /// gate snapshots with the settings hash, time by the database clock, one daily snapshot per subject and day). Raw SQL on purpose: no EF entity maps these tables (the runtime uses Npgsql), so the model snapshot does not change. A schema of its own, so the grants of the
    /// <c>confidence</c> schema are not touched. Both tables are insert-only for the runtime role, which therefore gets table privileges one by one and never the schema-wide helper (that one grants update and delete on every table).
    /// </summary>
    public partial class AddReadiness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS readiness");

            // 1. The owner's switch, stored like the kill switch of CF-12: append-only, the id drawn after an advisory lock so that the highest id is the last to commit.
            migrationBuilder.Sql("""
                CREATE TABLE readiness.settings (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    enforce boolean NOT NULL,
                    actor varchar(100) NOT NULL,
                    reason varchar(500) NOT NULL,
                    changed_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_readiness_settings PRIMARY KEY (id),
                    CONSTRAINT ck_readiness_settings_shape CHECK (actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0)
                )
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION readiness.stamp_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('readiness.settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('readiness.settings', 'id'));
                    NEW.changed_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_readiness_settings_stamp BEFORE INSERT ON readiness.settings FOR EACH ROW EXECUTE FUNCTION readiness.stamp_setting()");

            // 2. The snapshots. A daily snapshot is one per subject, strategy, version and India day; a gate snapshot is one per gate use (the system has no gate snapshot).
            migrationBuilder.Sql("""
                CREATE TABLE readiness.scorecard_snapshots (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    subject varchar(8) NOT NULL,
                    strategy_id varchar(200) NULL,
                    version integer NULL,
                    taken_on date NOT NULL,
                    taken_at timestamptz NOT NULL DEFAULT now(),
                    scope varchar(5) NOT NULL,
                    score double precision NOT NULL,
                    label varchar(10) NOT NULL,
                    body jsonb NOT NULL,
                    settings_hash char(64) NOT NULL,
                    CONSTRAINT pk_scorecard_snapshots PRIMARY KEY (id),
                    CONSTRAINT ck_scorecard_snapshots_subject CHECK (subject IN ('strategy', 'system') AND scope IN ('daily', 'gate') AND (subject = 'strategy') = (strategy_id IS NOT NULL) AND NOT (subject = 'system' AND (scope = 'gate' OR version IS NOT NULL))),
                    CONSTRAINT ck_scorecard_snapshots_shape CHECK (score >= 0 AND score <= 100 AND label IN ('not_ready', 'building', 'usable', 'ready') AND settings_hash ~ '^[0-9a-f]{64}$' AND jsonb_typeof(body) = 'object' AND (version IS NULL OR version >= 1))
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_scorecard_snapshots_daily ON readiness.scorecard_snapshots (subject, COALESCE(strategy_id, ''), COALESCE(version, 0), taken_on) WHERE scope = 'daily'");
            migrationBuilder.Sql("CREATE INDEX ix_scorecard_snapshots_subject_strategy_id ON readiness.scorecard_snapshots (subject, strategy_id, id DESC)");
            migrationBuilder.Sql("""
                CREATE FUNCTION readiness.stamp_snapshot() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    NEW.taken_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_scorecard_snapshots_stamp BEFORE INSERT ON readiness.scorecard_snapshots FOR EACH ROW EXECUTE FUNCTION readiness.stamp_snapshot()");

            // Runtime role: usage of the schema, then read and insert on each table and on nothing else (the default privileges of the database may grant more, so everything is revoked first). No update, no delete, no truncate.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        GRANT USAGE ON SCHEMA readiness TO xd_app;
                        REVOKE ALL ON TABLE readiness.settings, readiness.scorecard_snapshots FROM xd_app;
                        GRANT SELECT, INSERT ON TABLE readiness.settings, readiness.scorecard_snapshots TO xd_app;
                        EXECUTE format('GRANT USAGE ON SEQUENCE %s TO xd_app', pg_get_serial_sequence('readiness.settings', 'id'));
                        EXECUTE format('GRANT USAGE ON SEQUENCE %s TO xd_app', pg_get_serial_sequence('readiness.scorecard_snapshots', 'id'));
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS readiness CASCADE");
        }
    }
}
