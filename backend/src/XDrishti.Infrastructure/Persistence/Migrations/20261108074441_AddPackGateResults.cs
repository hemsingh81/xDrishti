using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The owner's record of the <c>verify-pack --gate</c> result of a strategy version (spec CF-11 row 15, ADR 0226): <c>readiness.pack_gate_results</c>, insert-only, id in commit order under an advisory lock, time by the database clock. Raw SQL on purpose (no EF entity maps the
    /// table; the model snapshot does not change). Table privileges go to the runtime role one by one, never the schema-wide helper (it would grant update and delete).
    /// </summary>
    public partial class AddPackGateResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE readiness.pack_gate_results (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    strategy_id varchar(200) NOT NULL,
                    version integer NOT NULL,
                    pack_id varchar(100) NOT NULL,
                    status varchar(30) NOT NULL,
                    trade_count integer NOT NULL,
                    actor varchar(100) NOT NULL,
                    reason varchar(500) NOT NULL,
                    recorded_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_pack_gate_results PRIMARY KEY (id),
                    CONSTRAINT ck_pack_gate_results_shape CHECK (
                        version >= 1 AND trade_count >= 0 AND length(btrim(strategy_id)) > 0 AND length(btrim(pack_id)) > 0 AND length(btrim(reason)) > 0
                        AND status IN ('PASS', 'PASS_WITH_DATA_FINDINGS', 'RERUN_NEEDED', 'DATA_REPAIR_NEEDED', 'BLOCKED_DEFECT')
                        AND actor LIKE 'owner:%' AND length(btrim(actor)) > 6)
                )
                """);
            migrationBuilder.Sql("CREATE INDEX ix_pack_gate_results_strategy_version ON readiness.pack_gate_results (strategy_id, version, id DESC)");
            migrationBuilder.Sql("""
                CREATE FUNCTION readiness.stamp_pack_gate_result() RETURNS trigger LANGUAGE plpgsql AS $fn$
                BEGIN
                    PERFORM pg_advisory_xact_lock(hashtextextended('readiness.pack_gate_results', 0));
                    NEW.id := nextval(pg_get_serial_sequence('readiness.pack_gate_results', 'id'));
                    NEW.recorded_at := now();
                    RETURN NEW;
                END
                $fn$
                """);
            migrationBuilder.Sql("CREATE TRIGGER trg_pack_gate_results_stamp BEFORE INSERT ON readiness.pack_gate_results FOR EACH ROW EXECUTE FUNCTION readiness.stamp_pack_gate_result()");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE ALL ON TABLE readiness.pack_gate_results FROM xd_app;
                        GRANT SELECT, INSERT ON TABLE readiness.pack_gate_results TO xd_app;
                        EXECUTE format('GRANT USAGE ON SEQUENCE %s TO xd_app', pg_get_serial_sequence('readiness.pack_gate_results', 'id'));
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS readiness.pack_gate_results");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS readiness.stamp_pack_gate_result()");
        }
    }
}
