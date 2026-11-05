using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The shadow record of every nightly candidate (spec CF-08 part A, ADR 0211): <c>paper.shadow_entries</c>, <c>shadow_sessions</c>, <c>shadow_grades</c> and the switch's <c>shadow_settings</c>, all insert-only for the runtime role. The database stamps
    /// <c>written_at</c> with its own clock (<c>paper.shadow_now()</c>), refuses a row stamped at or after 09:00 India time of its session, a row older than its plan and a chain that does not link to the newest session.
    /// </summary>
    public partial class AddPaperShadow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shadow_sessions",
                schema: "paper",
                columns: table => new
                {
                    session_date = table.Column<DateOnly>(type: "date", nullable: false),
                    n_rows = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    digest = table.Column<string>(type: "char(64)", nullable: false),
                    prev_digest = table.Column<string>(type: "char(64)", nullable: true),
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shadow_sessions", x => x.session_date);
                    table.CheckConstraint("ck_shadow_sessions_shape", "status IN ('ok', 'gap') AND n_rows >= 0 AND (status = 'ok' OR n_rows = 0) AND digest ~ '^[0-9a-f]{64}$' AND (prev_digest IS NULL OR prev_digest ~ '^[0-9a-f]{64}$')");
                });

            migrationBuilder.CreateTable(
                name: "shadow_settings",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shadow_settings", x => x.id);
                    table.CheckConstraint("ck_shadow_settings_shape", "actor LIKE 'owner:%' AND length(btrim(actor)) > 6 AND length(btrim(reason)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "shadow_entries",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    session_date = table.Column<DateOnly>(type: "date", nullable: false),
                    plan_id = table.Column<long>(type: "bigint", nullable: false),
                    plan_item_id = table.Column<long>(type: "bigint", nullable: false),
                    trade_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ticket_group = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    cell_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    strategy_hash = table.Column<string>(type: "char(64)", nullable: false),
                    symbol = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    side = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    entry_limit = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    stop = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    target = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    time_rule = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    config_hash = table.Column<string>(type: "char(64)", nullable: true),
                    model_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    p_win = table.Column<double>(type: "double precision", nullable: true),
                    feature_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    features = table.Column<string>(type: "jsonb", nullable: true),
                    counts_for_edge = table.Column<bool>(type: "boolean", nullable: false),
                    edge_epoch = table.Column<long>(type: "bigint", nullable: true),
                    written_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_hash = table.Column<string>(type: "char(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shadow_entries", x => x.id);
                    table.CheckConstraint("ck_shadow_entries_shape", "ticket_group IN ('primary', 'reserve', 'rejected') AND side IN ('BUY', 'SELL') AND row_hash ~ '^[0-9a-f]{64}$' AND (config_hash IS NULL OR config_hash ~ '^[0-9a-f]{64}$') AND (p_win IS NULL OR (p_win >= 0 AND p_win <= 1)) AND ((features IS NULL) = (feature_version IS NULL)) AND length(btrim(trade_key)) > 0");
                    table.ForeignKey(
                        name: "fk_shadow_entries_shadow_sessions_session_date",
                        column: x => x.session_date,
                        principalSchema: "paper",
                        principalTable: "shadow_sessions",
                        principalColumn: "session_date",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shadow_grades",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    entry_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    r_sim = table.Column<double>(type: "double precision", nullable: true),
                    r_basis = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: true),
                    entry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exit_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    exit_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    engine_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sheet_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    graded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shadow_grades", x => x.id);
                    table.CheckConstraint("ck_shadow_grades_shape", "status IN ('graded', 'not_triggered', 'not_gradable') AND ((status = 'graded') = (r_sim IS NOT NULL)) AND ((status = 'graded') = (r_basis IS NOT NULL)) AND (r_basis IS NULL OR r_basis IN ('net', 'gross')) AND (exit_at IS NULL OR entry_at IS NULL OR exit_at >= entry_at) AND length(btrim(engine_version)) > 0");
                    table.ForeignKey(
                        name: "fk_shadow_grades_shadow_entries_entry_id",
                        column: x => x.entry_id,
                        principalSchema: "paper",
                        principalTable: "shadow_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_shadow_entries_plan_id",
                schema: "paper",
                table: "shadow_entries",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "uq_shadow_entries_plan_item_id",
                schema: "paper",
                table: "shadow_entries",
                column: "plan_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_shadow_entries_session_trade_key",
                schema: "paper",
                table: "shadow_entries",
                columns: new[] { "session_date", "trade_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_shadow_grades_entry_id",
                schema: "paper",
                table: "shadow_grades",
                column: "entry_id",
                unique: true);

            // The ticket is in its plan (the composite key onto the plan record's unique pair, as paper.entries).
            migrationBuilder.Sql("ALTER TABLE paper.shadow_entries ADD CONSTRAINT fk_shadow_entries_item_in_plan FOREIGN KEY (plan_id, plan_item_id) REFERENCES trading.plan_items (plan_id, id) ON DELETE RESTRICT");

            // 1. The database's clock, in one function so that a test can fix it (the owner role replaces the function in a throw-away database; the runtime role cannot).
            migrationBuilder.Sql("""
                CREATE FUNCTION paper.shadow_now() RETURNS timestamptz LANGUAGE sql VOLATILE AS $fn$ SELECT clock_timestamp() $fn$
                """);

            // 2. A session joins the chain at the end, before the open for an ok session and after it for a gap. The advisory lock keeps two writers from both reading the same newest session.
            migrationBuilder.Sql("""
                CREATE FUNCTION paper.guard_shadow_session() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    cutoff timestamptz;
                    head_date date;
                    head_digest char(64);
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtext('paper.shadow_sessions'));
                    NEW.written_at := paper.shadow_now();
                    cutoff := (NEW.session_date + time '09:00') AT TIME ZONE 'Asia/Kolkata';
                    IF NEW.status = 'ok' AND NEW.written_at >= cutoff THEN
                        RAISE EXCEPTION 'A shadow session is written before 09:00 India time of its session' USING ERRCODE = '23514';
                    END IF;
                    IF NEW.status = 'gap' AND NEW.written_at < cutoff THEN
                        RAISE EXCEPTION 'A gap is recorded only for a session that has opened' USING ERRCODE = '23514';
                    END IF;

                    SELECT session_date, digest INTO head_date, head_digest FROM paper.shadow_sessions ORDER BY session_date DESC LIMIT 1;
                    IF head_date IS NULL THEN
                        IF NEW.prev_digest IS NOT NULL THEN
                            RAISE EXCEPTION 'The first shadow session has no previous digest' USING ERRCODE = '23514';
                        END IF;
                    ELSIF NEW.session_date <= head_date OR NEW.prev_digest IS DISTINCT FROM head_digest THEN
                        RAISE EXCEPTION 'The shadow chain only moves forward from its newest session' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_shadow_sessions_guard BEFORE INSERT ON paper.shadow_sessions FOR EACH ROW EXECUTE FUNCTION paper.guard_shadow_session()");

            // 3. A row is for the session of its plan, in an ok session, stamped before 09:00 India time of that session and not before its plan was stored. The cut-off is 09:00 (03:30 UTC), before arming (09:10) and the open (09:15).
            migrationBuilder.Sql("""
                CREATE FUNCTION paper.guard_shadow_entry() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    plan_for date;
                    plan_at timestamptz;
                    session_status text;
                BEGIN
                    NEW.written_at := paper.shadow_now();
                    SELECT for_date, created_at INTO plan_for, plan_at FROM trading.plans WHERE id = NEW.plan_id;
                    IF plan_for IS NULL OR plan_for <> NEW.session_date THEN
                        RAISE EXCEPTION 'A shadow row is for the session of its plan' USING ERRCODE = '23514';
                    END IF;
                    IF NEW.written_at >= (NEW.session_date + time '09:00') AT TIME ZONE 'Asia/Kolkata' THEN
                        RAISE EXCEPTION 'A shadow row is written before 09:00 India time of its session' USING ERRCODE = '23514';
                    END IF;
                    IF NEW.written_at < plan_at THEN
                        RAISE EXCEPTION 'A shadow row cannot be older than its plan' USING ERRCODE = '23514';
                    END IF;

                    SELECT status INTO session_status FROM paper.shadow_sessions WHERE session_date = NEW.session_date;
                    IF session_status IS DISTINCT FROM 'ok' THEN
                        RAISE EXCEPTION 'A shadow row needs an ok session of its date' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_shadow_entries_guard BEFORE INSERT ON paper.shadow_entries FOR EACH ROW EXECUTE FUNCTION paper.guard_shadow_entry()");

            // 4. A grade is stamped by the database and cannot be written before the open of its session (the grader of part B writes it after the close).
            migrationBuilder.Sql("""
                CREATE FUNCTION paper.guard_shadow_grade() RETURNS trigger LANGUAGE plpgsql AS $fn$
                DECLARE
                    entry_session date;
                BEGIN
                    NEW.graded_at := paper.shadow_now();
                    SELECT session_date INTO entry_session FROM paper.shadow_entries WHERE id = NEW.entry_id;
                    IF entry_session IS NOT NULL AND NEW.graded_at < (entry_session + time '09:00') AT TIME ZONE 'Asia/Kolkata' THEN
                        RAISE EXCEPTION 'A shadow grade is written after the open of its session' USING ERRCODE = '23514';
                    END IF;

                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_shadow_grades_guard BEFORE INSERT ON paper.shadow_grades FOR EACH ROW EXECUTE FUNCTION paper.guard_shadow_grade()");

            // 5. The time of a change of the switch is the database's.
            migrationBuilder.Sql("""
                CREATE FUNCTION paper.stamp_shadow_setting() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    NEW.changed_at := paper.shadow_now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_shadow_settings_stamp BEFORE INSERT ON paper.shadow_settings FOR EACH ROW EXECUTE FUNCTION paper.stamp_shadow_setting()");

            // Insert-only for the runtime role, table by table (the schema's earlier grant is not repeated: it would give UPDATE and DELETE back). The shadow tables are read by no other role.
            foreach (var table in new[] { "shadow_entries", "shadow_sessions", "shadow_grades", "shadow_settings" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Paper, table);
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Paper, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shadow_grades",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "shadow_settings",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "shadow_entries",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "shadow_sessions",
                schema: "paper");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS paper.stamp_shadow_setting()");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS paper.guard_shadow_grade()");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS paper.guard_shadow_entry()");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS paper.guard_shadow_session()");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS paper.shadow_now()");
        }
    }
}
