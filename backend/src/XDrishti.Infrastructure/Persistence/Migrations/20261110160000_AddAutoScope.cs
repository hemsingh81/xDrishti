using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The auto scope (spec AU-16, ADR 0251): schema <c>planning</c> with the insert-only <c>auto_scopes</c> (the newest row is in force, the runtime role cannot update or delete), and two nullable columns on <c>trading.plan_items</c> for the evidence a suggestion shows
    /// (<c>n_eff</c>, <c>stats_as_of</c>); a plan stored before them reads them as unavailable. Raw SQL: no EF entity maps these, so the model snapshot does not change.
    /// </summary>
    public partial class AddAutoScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS planning");
            migrationBuilder.Sql("""
                CREATE TABLE planning.auto_scopes (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    basket_ids uuid[] NOT NULL,
                    instrument_ids uuid[] NOT NULL,
                    scope_hash char(64) NOT NULL,
                    created_by varchar(100) NOT NULL,
                    created_at timestamptz NOT NULL,
                    CONSTRAINT pk_auto_scopes PRIMARY KEY (id),
                    CONSTRAINT ck_auto_scopes_shape CHECK (cardinality(basket_ids) + cardinality(instrument_ids) BETWEEN 1 AND 500 AND scope_hash ~ '^[0-9a-f]{64}$' AND created_by LIKE 'owner:%')
                )
                """);
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        GRANT USAGE ON SCHEMA planning TO xd_app;
                        REVOKE UPDATE, DELETE, TRUNCATE ON planning.auto_scopes FROM xd_app;
                        GRANT SELECT, INSERT ON planning.auto_scopes TO xd_app;
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql("ALTER TABLE trading.plan_items ADD COLUMN n_eff double precision NULL, ADD COLUMN stats_as_of date NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE trading.plan_items DROP COLUMN IF EXISTS n_eff, DROP COLUMN IF EXISTS stats_as_of");
            migrationBuilder.Sql("DROP TABLE IF EXISTS planning.auto_scopes");
        }
    }
}
