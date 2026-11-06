using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The kill rules of the decay monitor (spec CF-12 part 12b, ADR 0219): the schema <c>confidence</c> with <c>kill_settings</c> (the owner's switch <c>confidence.kill.auto</c>, append-only, id in commit order), <c>decay_checks</c> (one insert-only evaluation per strategy and day) and
    /// <c>kill_proposals</c> (insert, plus one transition proposed to applied or dismissed that touches four columns). Raw SQL on purpose: no EF entity maps these tables (the runtime uses Npgsql), so the model snapshot does not change.
    /// The database itself refuses what the application must not do: a proposal-only rule (the CF-09 failure boundary) decided by <c>auto</c>, an automatic decision while the newest switch row is not ON, a decided proposal changed again, and any edit or delete by the runtime role.
    /// </summary>
    public partial class AddDecayAndKill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS confidence");

            // 1. The owner's switch, stored like paper.shadow_settings: append-only, the id drawn after an advisory lock so that the highest id is the last to commit.
            migrationBuilder.Sql("""
                CREATE TABLE confidence.kill_settings (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    auto boolean NOT NULL,
                    actor varchar(100) NOT NULL,
                    reason varchar(500) NOT NULL,
                    changed_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_kill_settings PRIMARY KEY (id),
                    CONSTRAINT ck_kill_settings_shape CHECK (actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0)
                )
                """);
            migrationBuilder.Sql("""
                CREATE FUNCTION confidence.stamp_kill_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('confidence.kill_settings', 0));
                    NEW.id := nextval(pg_get_serial_sequence('confidence.kill_settings', 'id'));
                    NEW.changed_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_kill_settings_stamp BEFORE INSERT ON confidence.kill_settings FOR EACH ROW EXECUTE FUNCTION confidence.stamp_kill_setting()");

            // 2. The proposals. One per strategy, rule and India day. proposal_only is the CF-09 failure boundary and nothing else.
            migrationBuilder.Sql("""
                CREATE TABLE confidence.kill_proposals (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    strategy varchar(200) NOT NULL,
                    epoch_id bigint NULL,
                    rule varchar(24) NOT NULL,
                    proposal_only boolean NOT NULL,
                    value double precision NULL,
                    limit_value double precision NULL,
                    trades integer NOT NULL,
                    text varchar(1000) NOT NULL,
                    cells jsonb NOT NULL,
                    settings_hash char(64) NOT NULL,
                    series_trades integer NOT NULL,
                    proposed_on date NOT NULL,
                    proposed_at timestamptz NOT NULL DEFAULT now(),
                    state varchar(9) NOT NULL DEFAULT 'proposed',
                    actor varchar(100) NULL,
                    decided_at timestamptz NULL,
                    auto_setting_id bigint NULL,
                    CONSTRAINT pk_kill_proposals PRIMARY KEY (id),
                    CONSTRAINT fk_kill_proposals_kill_settings_auto_setting_id FOREIGN KEY (auto_setting_id) REFERENCES confidence.kill_settings (id) ON DELETE RESTRICT,
                    CONSTRAINT ck_kill_proposals_rule CHECK (rule IN ('cost_stress', 'drawdown', 'decay_twice', 'failure_boundary') AND proposal_only = (rule = 'failure_boundary')),
                    CONSTRAINT ck_kill_proposals_shape CHECK (state IN ('proposed', 'applied', 'dismissed') AND settings_hash ~ '^[0-9a-f]{64}$' AND jsonb_typeof(cells) = 'array' AND trades >= 0 AND series_trades >= 0),
                    CONSTRAINT ck_kill_proposals_decision CHECK (
                        ((state = 'proposed') = (actor IS NULL)) AND ((state = 'proposed') = (decided_at IS NULL))
                        AND (actor IS NULL OR actor = 'auto' OR (actor LIKE 'owner:%' AND length(btrim(actor)) > 6))
                        AND ((actor IS NOT DISTINCT FROM 'auto') = (auto_setting_id IS NOT NULL))
                        AND NOT (actor = 'auto' AND (proposal_only OR state <> 'applied'))
                    )
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_kill_proposals_strategy_rule_day ON confidence.kill_proposals (strategy, rule, proposed_on)");
            migrationBuilder.Sql("CREATE INDEX ix_kill_proposals_state_id ON confidence.kill_proposals (state, id DESC)");
            migrationBuilder.Sql("CREATE INDEX ix_kill_proposals_strategy_rule_id ON confidence.kill_proposals (strategy, rule, id DESC)");
            migrationBuilder.Sql("""
                CREATE FUNCTION confidence.guard_kill_proposal() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF OLD.state <> 'proposed' THEN
                        RAISE EXCEPTION 'A decided kill proposal is not changed' USING ERRCODE = '23514';
                    END IF;

                    IF NEW.state = 'proposed' THEN
                        RAISE EXCEPTION 'A kill proposal only moves on, from proposed to applied or dismissed' USING ERRCODE = '23514';
                    END IF;

                    IF NEW.actor = 'auto' AND NOT EXISTS (
                        SELECT 1 FROM confidence.kill_settings s
                        WHERE s.id = NEW.auto_setting_id AND s.auto AND s.id = (SELECT max(id) FROM confidence.kill_settings)) THEN
                        RAISE EXCEPTION 'An automatic decision needs the newest row of the switch to be ON and to be the one recorded' USING ERRCODE = '23514';
                    END IF;

                    NEW.decided_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_kill_proposals_guard BEFORE UPDATE ON confidence.kill_proposals FOR EACH ROW EXECUTE FUNCTION confidence.guard_kill_proposal()");

            // 3. The evaluations of the decay monitor, one per strategy and India day, with the settings hash and the four verdicts.
            migrationBuilder.Sql("""
                CREATE TABLE confidence.decay_checks (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    strategy varchar(200) NOT NULL,
                    epoch_id bigint NULL,
                    checked_on date NOT NULL,
                    evaluated_at timestamptz NOT NULL DEFAULT now(),
                    settings_hash char(64) NOT NULL,
                    edge_state varchar(16) NOT NULL,
                    trades integer NOT NULL,
                    verdicts jsonb NOT NULL,
                    CONSTRAINT pk_decay_checks PRIMARY KEY (id),
                    CONSTRAINT ck_decay_checks_shape CHECK (settings_hash ~ '^[0-9a-f]{64}$' AND trades >= 0 AND jsonb_typeof(verdicts) = 'array')
                )
                """);
            migrationBuilder.Sql("CREATE UNIQUE INDEX uq_decay_checks_strategy_day ON confidence.decay_checks (strategy, checked_on)");

            // Runtime role: usage of the schema, then table by table. kill_settings and decay_checks are insert-only; kill_proposals is insert-only except the four decision columns (a column grant), and nothing is ever deleted.
            migrationBuilder.GrantSchemaUsageToAppRole(Schemas.Confidence);
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Confidence, "kill_settings");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Confidence, "decay_checks");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Confidence, "kill_proposals");
            migrationBuilder.GrantColumnUpdateToAppRole(Schemas.Confidence, "kill_proposals", "state", "actor", "decided_at", "auto_setting_id");

            // The runtime role can only insert an open proposal: INSERT is granted on the columns of the proposal itself, so it cannot name state, actor, decided_at or auto_setting_id (an already-decided row). The defaults make the row proposed.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE INSERT ON confidence.kill_proposals FROM xd_app;
                        GRANT INSERT (strategy, epoch_id, rule, proposal_only, value, limit_value, trades, text, cells, settings_hash, series_trades, proposed_on) ON confidence.kill_proposals TO xd_app;
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        EXECUTE format('GRANT USAGE ON SEQUENCE %s TO xd_app', pg_get_serial_sequence('confidence.kill_settings', 'id'));
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS confidence CASCADE");
        }
    }
}
