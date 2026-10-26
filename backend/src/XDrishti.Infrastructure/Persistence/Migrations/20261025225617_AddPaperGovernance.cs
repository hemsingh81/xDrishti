using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaperGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "config_snapshots",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    taken_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    session_date = table.Column<DateOnly>(type: "date", nullable: false),
                    hash = table.Column<string>(type: "char(64)", nullable: false),
                    components = table.Column<string>(type: "jsonb", nullable: false),
                    partial = table.Column<bool>(type: "boolean", nullable: false),
                    process = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_config_snapshots", x => x.id);
                    table.CheckConstraint("ck_config_snapshots_shape", "length(hash) = 64 AND length(btrim(process)) > 0");
                });

            migrationBuilder.CreateTable(
                name: "config_changes",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    from_snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    to_snapshot_id = table.Column<long>(type: "bigint", nullable: false),
                    key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    @class = table.Column<string>(name: "class", type: "character varying(10)", maxLength: 10, nullable: false),
                    before = table.Column<string>(type: "text", nullable: true),
                    after = table.Column<string>(type: "text", nullable: true),
                    effect = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    epoch_id = table.Column<long>(type: "bigint", nullable: false),
                    explained_by = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_config_changes", x => x.id);
                    table.CheckConstraint("ck_config_changes_shape", "class IN ('frozen', 'risk_down', 'free') AND effect IN ('new_epoch', 'noted')");
                    table.ForeignKey(
                        name: "fk_config_changes_config_snapshots_from_snapshot_id",
                        column: x => x.from_snapshot_id,
                        principalSchema: "paper",
                        principalTable: "config_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_config_changes_config_snapshots_to_snapshot_id",
                        column: x => x.to_snapshot_id,
                        principalSchema: "paper",
                        principalTable: "config_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_config_changes_epochs_epoch_id",
                        column: x => x.epoch_id,
                        principalSchema: "paper",
                        principalTable: "epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "epoch_notes",
                schema: "paper",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    epoch_id = table.Column<long>(type: "bigint", nullable: false),
                    change_id = table.Column<long>(type: "bigint", nullable: true),
                    hypothesis_id = table.Column<long>(type: "bigint", nullable: true),
                    text = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    actor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_epoch_notes", x => x.id);
                    table.CheckConstraint("ck_epoch_notes_shape", "length(btrim(text)) > 0 AND length(btrim(actor)) > 0");
                    table.ForeignKey(
                        name: "fk_epoch_notes_config_changes_change_id",
                        column: x => x.change_id,
                        principalSchema: "paper",
                        principalTable: "config_changes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_epoch_notes_epochs_epoch_id",
                        column: x => x.epoch_id,
                        principalSchema: "paper",
                        principalTable: "epochs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_config_changes_epoch_id",
                schema: "paper",
                table: "config_changes",
                column: "epoch_id");

            migrationBuilder.CreateIndex(
                name: "ix_config_changes_from_snapshot_id",
                schema: "paper",
                table: "config_changes",
                column: "from_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_config_changes_to_snapshot_id",
                schema: "paper",
                table: "config_changes",
                column: "to_snapshot_id");

            migrationBuilder.CreateIndex(
                name: "ix_config_snapshots_taken_at",
                schema: "paper",
                table: "config_snapshots",
                column: "taken_at");

            migrationBuilder.CreateIndex(
                name: "uq_config_snapshots_session_hash",
                schema: "paper",
                table: "config_snapshots",
                columns: new[] { "session_date", "hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_epoch_notes_change_id",
                schema: "paper",
                table: "epoch_notes",
                column: "change_id");

            migrationBuilder.CreateIndex(
                name: "ix_epoch_notes_epoch_id",
                schema: "paper",
                table: "epoch_notes",
                column: "epoch_id");

            // Insert-only for the runtime role (ADR 0119). Granted per table: the schema-wide grant would give back UPDATE and DELETE on the capture tables.
            foreach (var table in new[] { "config_snapshots", "config_changes", "epoch_notes" })
            {
                migrationBuilder.GrantTableToAppRole(Schemas.Paper, table);
                migrationBuilder.MakeAppendOnlyForAppRole(Schemas.Paper, table);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "epoch_notes",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "config_changes",
                schema: "paper");

            migrationBuilder.DropTable(
                name: "config_snapshots",
                schema: "paper");
        }
    }
}
