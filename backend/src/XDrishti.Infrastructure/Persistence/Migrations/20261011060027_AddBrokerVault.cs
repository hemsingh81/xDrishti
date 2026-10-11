using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerVault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ADR 0008: credential references (secret files mounted from the Keychain) are replaced by the encrypted broker vault.
            // Existing accounts keep everything else and start without secrets ("API key: not set").
            migrationBuilder.DropIndex(
                name: "ux_broker_accounts_credential_ref_active",
                schema: "accounts",
                table: "broker_accounts");

            migrationBuilder.DropColumn(
                name: "credential_ref",
                schema: "accounts",
                table: "broker_accounts");

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

            migrationBuilder.AddColumn<DateOnly>(
                name: "api_key_issued_on",
                schema: "accounts",
                table: "broker_accounts",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "broker_secrets",
                schema: "accounts",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_secrets", x => new { x.account_id, x.name });
                    table.ForeignKey(
                        name: "fk_broker_secrets_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Plain DML for the runtime role (stated explicitly; not a re-grant of the whole schema, which would undo the
            // append-only rule of account_events).
            migrationBuilder.GrantTableToAppRole(Schemas.Accounts, "broker_secrets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broker_secrets",
                schema: "accounts");

            migrationBuilder.DropColumn(
                name: "api_key_issued_on",
                schema: "accounts",
                table: "broker_accounts");

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

            migrationBuilder.AddColumn<string>(
                name: "credential_ref",
                schema: "accounts",
                table: "broker_accounts",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            // Every active account needs a distinct reference again before the unique index returns.
            migrationBuilder.Sql("UPDATE accounts.broker_accounts SET credential_ref = left(replace(slug, '-', '_'), 31) || '_' || left(md5(id::text), 8)");

            migrationBuilder.CreateIndex(
                name: "ux_broker_accounts_credential_ref_active",
                schema: "accounts",
                table: "broker_accounts",
                column: "credential_ref",
                unique: true,
                filter: "archived_at IS NULL");
        }
    }
}
