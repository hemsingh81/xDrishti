using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "weekly_reviews",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    iso_week = table.Column<string>(type: "text", nullable: false),
                    epoch_id = table.Column<long>(type: "bigint", nullable: false),
                    built_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    built_by = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    sections = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_weekly_reviews", x => x.id);
                    table.CheckConstraint("ck_weekly_reviews_shape", "iso_week ~ '^[0-9]{4}-W[0-9]{2}$' AND text LIKE 'Paper only: no order was placed and none exists.%' AND length(btrim(built_by)) > 0");
                    table.ForeignKey(
                        name: "fk_weekly_reviews_epochs_epoch_id",
                        column: x => x.epoch_id,
                        principalSchema: "paper",
                        principalTable: "epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_weekly_reviews_epoch_id",
                schema: "paper",
                table: "weekly_reviews",
                column: "epoch_id");

            migrationBuilder.CreateIndex(
                name: "uq_weekly_reviews_week_epoch",
                schema: "paper",
                table: "weekly_reviews",
                columns: new[] { "iso_week", "epoch_id" },
                unique: true);

            // Insert-only for the runtime role (ADR 0121). Granted on its own table: the schema-wide grant would give back UPDATE and DELETE on the capture tables.
            migrationBuilder.GrantTableToAppRole(Schemas.Paper, "weekly_reviews");
            migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Paper, "weekly_reviews");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "weekly_reviews",
                schema: "paper");
        }
    }
}
