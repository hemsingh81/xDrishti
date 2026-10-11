using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // P1-16c / ADR 0008: connecting accounts to their brokers. Tokens themselves live encrypted in broker_secrets.
            migrationBuilder.CreateTable(
                name: "broker_connections",
                schema: "accounts",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    broker_user_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    broker_user_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    last_connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_checked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_broker_connections", x => x.account_id);
                    table.CheckConstraint("ck_broker_connections_status", "status IN ('NotConnected', 'Connected', 'Invalid')");
                    table.ForeignKey(
                        name: "fk_broker_connections_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pending_connects",
                schema: "accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    broker = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    state_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pending_connects", x => x.id);
                    table.ForeignKey(
                        name: "fk_pending_connects_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pending_connects_account_id_created_at",
                schema: "accounts",
                table: "pending_connects",
                columns: new[] { "account_id", "created_at" });

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

            // Plain DML for the runtime role, table by table (a re-grant of the whole schema would undo the append-only rule of account_events).
            migrationBuilder.GrantTableToAppRole(Schemas.Accounts, "broker_connections");
            migrationBuilder.GrantTableToAppRole(Schemas.Accounts, "pending_connects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broker_connections",
                schema: "accounts");

            migrationBuilder.DropTable(
                name: "pending_connects",
                schema: "accounts");
        }
    }
}
