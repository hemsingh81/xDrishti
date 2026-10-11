using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropUnusedBrokerConnectionColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0009: Dhan only, consent flow only. The tables are empty (owner reset on 2026-10-11); this clears what could still be there
            // from the removed Upstox / extended-token paths before the columns are tightened (children of an account go with it).
            migrationBuilder.Sql("DELETE FROM accounts.account_events WHERE event IN ('TokenPasted', 'TokenRenewed', 'BrokerConnectionTested') OR account_id IN (SELECT id FROM accounts.broker_accounts WHERE broker <> 'Dhan');");
            migrationBuilder.Sql("DELETE FROM accounts.broker_accounts WHERE broker <> 'Dhan';");
            // Rows that cannot satisfy the NOT NULL columns below would otherwise get made-up values (empty client id, year 0001 key date).
            migrationBuilder.Sql("DELETE FROM accounts.account_events WHERE account_id IN (SELECT id FROM accounts.broker_accounts WHERE client_id IS NULL OR api_key_issued_on IS NULL);");
            migrationBuilder.Sql("DELETE FROM accounts.broker_accounts WHERE client_id IS NULL OR api_key_issued_on IS NULL;");
            migrationBuilder.Sql("DELETE FROM accounts.broker_secrets WHERE name = 'extended_token';");

            // The status `Invalid` is gone (nothing sets it any more).
            migrationBuilder.Sql("UPDATE accounts.broker_connections SET status = 'NotConnected' WHERE status = 'Invalid';");
            migrationBuilder.DropCheckConstraint(
                name: "ck_broker_connections_status",
                schema: "accounts",
                table: "broker_connections");

            migrationBuilder.AddCheckConstraint(
                name: "ck_broker_connections_status",
                schema: "accounts",
                table: "broker_connections",
                sql: "status IN ('NotConnected', 'Connected')");

            migrationBuilder.DropIndex(
                name: "ix_pending_connects_session_id_broker_created_at",
                schema: "accounts",
                table: "pending_connects");

            migrationBuilder.DropIndex(
                name: "ix_pending_connects_state_hash",
                schema: "accounts",
                table: "pending_connects");

            migrationBuilder.DropColumn(
                name: "broker",
                schema: "accounts",
                table: "pending_connects");

            migrationBuilder.DropColumn(
                name: "state_hash",
                schema: "accounts",
                table: "pending_connects");

            migrationBuilder.DropColumn(
                name: "last_checked_at",
                schema: "accounts",
                table: "broker_connections");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "accounts",
                table: "broker_connections");

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                schema: "accounts",
                table: "broker_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "api_key_issued_on",
                schema: "accounts",
                table: "broker_accounts",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1),
                oldClrType: typeof(DateOnly),
                oldType: "date",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pending_connects_session_id_created_at",
                schema: "accounts",
                table: "pending_connects",
                columns: new[] { "session_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_broker_connections_status",
                schema: "accounts",
                table: "broker_connections");

            migrationBuilder.AddCheckConstraint(
                name: "ck_broker_connections_status",
                schema: "accounts",
                table: "broker_connections",
                sql: "status IN ('NotConnected', 'Connected', 'Invalid')");

            migrationBuilder.DropIndex(
                name: "ix_pending_connects_session_id_created_at",
                schema: "accounts",
                table: "pending_connects");

            migrationBuilder.AddColumn<string>(
                name: "broker",
                schema: "accounts",
                table: "pending_connects",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "state_hash",
                schema: "accounts",
                table: "pending_connects",
                type: "character(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_checked_at",
                schema: "accounts",
                table: "broker_connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                schema: "accounts",
                table: "broker_connections",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "client_id",
                schema: "accounts",
                table: "broker_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<DateOnly>(
                name: "api_key_issued_on",
                schema: "accounts",
                table: "broker_accounts",
                type: "date",
                nullable: true,
                oldClrType: typeof(DateOnly),
                oldType: "date");

            migrationBuilder.CreateIndex(
                name: "ix_pending_connects_session_id_broker_created_at",
                schema: "accounts",
                table: "pending_connects",
                columns: new[] { "session_id", "broker", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pending_connects_state_hash",
                schema: "accounts",
                table: "pending_connects",
                column: "state_hash",
                unique: true,
                filter: "state_hash IS NOT NULL");
        }
    }
}
