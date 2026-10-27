using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiReadAccessHardening : Migration
    {
        /// <summary>
        /// Hardening of the assistant's roles after review (spec P9-02, ADR 0137 item 10). The roles are plain members of PUBLIC, so what PUBLIC holds is theirs: (1) TEMPORARY on the database (a temp table is a place to build things and a way to keep state between
        /// statements), (2) USAGE on schema public (the earlier role-level REVOKE could not remove it), (3) EXECUTE on the advisory-lock functions (a lock held by a question could stall the application's own locks); pg_advisory_unlock_all stays, because the driver calls it every time a connection returns to its pool and it only releases the caller's own locks. All three are taken from PUBLIC and given back to the
        /// runtime role, which needs them (temp tables, extension functions, the locks of the settings triggers). The owner and superusers are not affected. Where the migrating role is not a superuser the function part is skipped with a notice.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    f oid;
                BEGIN
                    EXECUTE format('REVOKE TEMPORARY ON DATABASE %I FROM PUBLIC', current_database());
                    REVOKE USAGE ON SCHEMA public FROM PUBLIC;
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                        EXECUTE format('GRANT TEMPORARY ON DATABASE %I TO xd_app', current_database());
                        GRANT USAGE ON SCHEMA public TO xd_app;
                    END IF;
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = current_user AND rolsuper) THEN
                        FOR f IN SELECT p.oid FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'pg_catalog' AND p.proname ~ '^pg_(try_)?advisory' AND p.proname <> 'pg_advisory_unlock_all' LOOP
                            EXECUTE format('REVOKE EXECUTE ON FUNCTION %s FROM PUBLIC', f::regprocedure);
                            IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_app') THEN
                                EXECUTE format('GRANT EXECUTE ON FUNCTION %s TO xd_app', f::regprocedure);
                            END IF;
                        END LOOP;
                    ELSE
                        RAISE NOTICE 'The advisory-lock functions were not restricted: the migrating role is not a superuser.';
                    END IF;
                END $$
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    f oid;
                BEGIN
                    EXECUTE format('GRANT TEMPORARY ON DATABASE %I TO PUBLIC', current_database());
                    GRANT USAGE ON SCHEMA public TO PUBLIC;
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = current_user AND rolsuper) THEN
                        FOR f IN SELECT p.oid FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'pg_catalog' AND p.proname ~ '^pg_(try_)?advisory' AND p.proname <> 'pg_advisory_unlock_all' LOOP
                            EXECUTE format('GRANT EXECUTE ON FUNCTION %s TO PUBLIC', f::regprocedure);
                        END LOOP;
                    END IF;
                END $$
                """);
        }
    }
}
