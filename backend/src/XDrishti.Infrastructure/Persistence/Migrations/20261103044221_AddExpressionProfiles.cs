using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExpressionProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P14-06, ADR 0193: the pre-registered ways a cash signal becomes an instrument expression, content-addressed by the hash of their canonical text. Raw SQL on purpose (nothing maps this table in the EF model; the store writes with SQL).
            migrationBuilder.Sql("""
                CREATE TABLE lab.expression_profiles (
                    profile_hash char(64) NOT NULL,
                    kind varchar(16) NOT NULL,
                    params jsonb NOT NULL,
                    canonical_text text NOT NULL,
                    created_at timestamptz NOT NULL DEFAULT now(),
                    CONSTRAINT pk_expression_profiles PRIMARY KEY (profile_hash),
                    CONSTRAINT ck_expression_profiles_hash CHECK (profile_hash ~ '^[0-9a-f]{64}$'),
                    CONSTRAINT ck_expression_profiles_kind CHECK (kind IN ('cash', 'future', 'option_buy', 'debit_spread', 'credit_spread', 'iron_condor', 'pair', 'basis')),
                    CONSTRAINT ck_expression_profiles_params CHECK (jsonb_typeof(params) = 'object')
                )
                """);

            // Insert-only for the runtime role, granted table by table (a schema-wide re-grant would give back UPDATE and DELETE on the append-only tables of lab). The execution role and the MCP roles are granted nothing.
            migrationBuilder.GrantTableToAppRole(Schemas.Lab, "expression_profiles");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Lab, "expression_profiles");

            // An eight-part key of 64-character parts is 519 characters. Widening a varchar changes no row and rewrites no key. The read-only view aiv.hypotheses selects the column, and Postgres refuses to change the type of a column a view uses: the view is
            // dropped and made again with the same text, and the grants of AddAiReadAccess are given again (a view made now gets the owner's default DML for xd_app, which is taken away as every aiv migration must).
            migrationBuilder.Sql("DROP VIEW aiv.hypotheses");
            migrationBuilder.AlterColumn<string>(
                name: "cell_key",
                schema: "research",
                table: "hypotheses",
                type: "character varying(600)",
                maxLength: 600,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(400)",
                oldMaxLength: 400,
                oldNullable: true);
            CreateHypothesesView(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS lab.expression_profiles");

            // Narrow the column only when no stored key is longer than 400 characters: the log is insert-only and a key is never cut. The view goes and comes back around the change, as in Up.
            migrationBuilder.Sql("DROP VIEW aiv.hypotheses");
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM research.hypotheses WHERE length(cell_key) > 400) THEN
                        ALTER TABLE research.hypotheses ALTER COLUMN cell_key TYPE character varying(400);
                    END IF;
                END $$;
                """);
            CreateHypothesesView(migrationBuilder);
        }

        // The text of AddAiReadAccess, unchanged, with the grants of that migration for this one view.
        private static void CreateHypothesesView(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE VIEW aiv.hypotheses AS
                    SELECT
                        t.hypothesis_id AS hypothesis_id,
                        t.state AS state,
                        t.from_state AS from_state,
                        t.source AS source,
                        t.cell_key AS cell_key,
                        t.knob_name AS knob_name,
                        t.knob_kind AS knob_kind,
                        t.baseline_value AS baseline_value,
                        t.candidate_value AS candidate_value,
                        t.rationale AS rationale,
                        t.baseline_run_key AS baseline_run_key,
                        t.candidate_run_key AS candidate_run_key,
                        t.experiment_id AS experiment_id,
                        t.reason AS reason,
                        t.created_at AS created_at,
                        t.is_seed AS is_seed
                    FROM research.hypotheses t
                """);
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    r text;
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE ALL ON aiv.hypotheses FROM xd_app;
                    END IF;
                    REVOKE ALL ON aiv.hypotheses FROM PUBLIC;
                    FOREACH r IN ARRAY ARRAY['xd_mcp_read', 'xd_mcp_sql'] LOOP
                        IF EXISTS (SELECT FROM pg_roles WHERE rolname = r) THEN
                            EXECUTE format('GRANT SELECT ON aiv.hypotheses TO %I', r);
                        END IF;
                    END LOOP;
                END $$;
                """);
        }
    }
}
