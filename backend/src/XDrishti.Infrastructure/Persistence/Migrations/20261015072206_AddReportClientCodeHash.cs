using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportClientCodeHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_client_codes",
                schema: "accounts",
                columns: table => new
                {
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    remembered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_client_codes", x => x.account_id);
                    table.CheckConstraint("ck_report_client_codes_hash", "length(hash) = 64");
                    table.ForeignKey(
                        name: "fk_report_client_codes_broker_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "accounts",
                        principalTable: "broker_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // The hash of the client code is remembered once and kept for good: the runtime role can read and insert it, never change or delete it.
            migrationBuilder.GrantTableToAppRole(Schemas.Accounts, "report_client_codes");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Accounts, "report_client_codes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_client_codes",
                schema: "accounts");
        }
    }
}
