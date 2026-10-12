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

    /// <summary>
    /// Takes DELETE away from the runtime role on a table whose rows are kept for good (it can still read, insert and update). Call after
    /// <see cref="GrantSchemaUsageToAppRole"/> and after any later migration that re-grants the schema. No-op when the role does not exist (e.g. integration-test databases).
    /// </summary>
    public static void NeverDeleteForAppRole(this MigrationBuilder migrationBuilder, string schema, string table) =>
        migrationBuilder.Sql($"""
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{AppRole}') THEN
                    REVOKE DELETE ON {schema}.{table} FROM {AppRole};
                END IF;
            END $$;
            """);

    /// <summary>
    /// Lets only the runtime role (and the function's owner) execute a function: <c>EXECUTE</c> is taken from <c>PUBLIC</c> (where PostgreSQL grants it by default) and given to the runtime role. For the
    /// narrow <c>SECURITY DEFINER</c> functions that do what the runtime role may not do itself. <paramref name="signature"/> is the function with its argument types, for example
    /// <c>market.undo_csv_bars(uuid, timestamptz, timestamptz)</c>. The revoke always runs; the grant is a no-op when the role does not exist (e.g. integration-test databases).
    /// </summary>
    public static void RestrictFunctionToAppRole(this MigrationBuilder migrationBuilder, string signature) =>
        migrationBuilder.Sql($"""
            REVOKE ALL ON FUNCTION {signature} FROM PUBLIC;
            DO $$
            BEGIN
                IF EXISTS (SELECT FROM pg_roles WHERE rolname = '{AppRole}') THEN
                    GRANT EXECUTE ON FUNCTION {signature} TO {AppRole};
                END IF;
            END $$;
            """);
}
