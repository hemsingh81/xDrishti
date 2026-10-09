using Microsoft.EntityFrameworkCore.Migrations;

namespace XDrishti.Infrastructure.Persistence;

/// <summary>Reusable steps for hand-written parts of migrations.</summary>
internal static class MigrationBuilderExtensions
{
    /// <summary>Least-privilege runtime role used by the API and workers (created by deploy/db/init).</summary>
    public const string AppRole = "xd_app";

    /// <summary>
    /// Lets the runtime role use a schema. Table privileges come from default privileges set at database init.
    /// No-op when the role does not exist (e.g. integration-test databases).
    /// </summary>
    public static void GrantSchemaUsageToAppRole(this MigrationBuilder migrationBuilder, string schema) =>
        migrationBuilder.Sql($"""
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{AppRole}') THEN
                    GRANT USAGE ON SCHEMA {schema} TO {AppRole};
                    GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {AppRole};
                END IF;
            END $$;
            """);
}
