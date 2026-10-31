using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The sealed same-day token hand-over to the execution service (ADR 0171): <c>accounts.exec_token_handover</c>, one row per account, written and deleted by the API
    /// (xd_app keeps its default rights) together with the vault's access token. <c>xd_exec</c> gets SELECT on this table only; it still has nothing on
    /// <c>accounts.broker_secrets</c>. No other role reads it (PUBLIC holds nothing; the assistant's read-only roles see only their views).
    /// </summary>
    public partial class AddExecTokenHandover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exec_token_handover",
                schema: "accounts",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sealed_token = table.Column<byte[]>(type: "bytea", nullable: false),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exec_token_handover", x => x.account_id);
                    table.ForeignKey(
                        name: "fk_exec_token_handover_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                REVOKE ALL ON accounts.exec_token_handover FROM PUBLIC;
                DO $$
                BEGIN
                    IF EXISTS (SELECT FROM pg_roles WHERE rolname = 'xd_exec') THEN
                        GRANT SELECT ON accounts.exec_token_handover TO xd_exec;
                    END IF;
                END $$
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the table drops its grants.
            migrationBuilder.DropTable(
                name: "exec_token_handover",
                schema: "accounts");
        }
    }
}
