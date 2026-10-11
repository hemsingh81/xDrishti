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

    /// <summary>
    /// Gives the runtime role plain DML (read, insert, update, delete) on one table, without touching the other tables of the schema.
    /// No-op when the role does not exist (e.g. integration-test databases).
    /// </summary>
    public static void GrantTableToAppRole(this MigrationBuilder migrationBuilder, string schema, string table) =>
        migrationBuilder.Sql($"""
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{AppRole}') THEN
                    GRANT SELECT, INSERT, UPDATE, DELETE ON {schema}.{table} TO {AppRole};
                END IF;
            END $$;
            """);

    /// <summary>
    /// Makes a table append-only for the runtime role (it can still read and insert). Call after <see cref="GrantSchemaUsageToAppRole"/>
    /// and after any later migration that re-grants the schema. No-op when the role does not exist (e.g. integration-test databases).
    /// </summary>
    public static void MakeAppendOnlyForAppRole(this MigrationBuilder migrationBuilder, string schema, string table) =>
        migrationBuilder.Sql($"""
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{AppRole}') THEN
                    REVOKE UPDATE, DELETE ON {schema}.{table} FROM {AppRole};
                END IF;
            END $$;
            """);
}
