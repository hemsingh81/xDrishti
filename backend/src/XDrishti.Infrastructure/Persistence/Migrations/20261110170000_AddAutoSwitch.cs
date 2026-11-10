using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The Auto switch log (spec AU-17, ADR 0252): the insert-only <c>planning.auto_switch_changes</c>, one row per ON or OFF with who (the owner, or <c>auto</c> for an automatic step, never an approval), when, the services that change resumed or paused, and the setting snapshot.
    /// The runtime role can read and insert, not update or delete. Raw SQL: no EF entity maps it, so the model snapshot does not change.
    /// </summary>
    public partial class AddAutoSwitch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE planning.auto_switch_changes (
                    id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
                    kind varchar(8) NOT NULL,
                    actor varchar(100) NOT NULL,
                    at timestamptz NOT NULL,
                    changed varchar(100)[] NOT NULL,
                    snapshot jsonb NOT NULL,
                    CONSTRAINT pk_auto_switch_changes PRIMARY KEY (id),
                    CONSTRAINT ck_auto_switch_changes_shape CHECK (kind IN ('on', 'off') AND (actor LIKE 'owner:%' OR actor = 'auto') AND cardinality(changed) <= 20)
                )
                """);
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        REVOKE UPDATE, DELETE, TRUNCATE ON planning.auto_switch_changes FROM xd_app;
                        GRANT SELECT, INSERT ON planning.auto_switch_changes TO xd_app;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS planning.auto_switch_changes");
        }
    }
}
