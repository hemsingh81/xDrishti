using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAssistantProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_proposals",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    dedupe_key = table.Column<string>(type: "character(64)", nullable: false),
                    created_on = table.Column<DateOnly>(type: "date", nullable: false),
                    subject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    reason = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    checks_json = table.Column<string>(type: "jsonb", nullable: false),
                    pack_hash = table.Column<string>(type: "character(64)", nullable: false),
                    model_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    model_sha256 = table.Column<string>(type: "character(64)", nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_by = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_proposals", x => x.id);
                    table.CheckConstraint("ck_assistant_proposals_kind", "kind IN ('hypothesis', 'basket_add', 'basket_remove')");
                    table.CheckConstraint("ck_assistant_proposals_shape", "length(dedupe_key) = 64 AND length(btrim(reason)) > 0 AND length(btrim(subject)) > 0 AND jsonb_typeof(payload) = 'object' AND expires_at > created_at");
                });

            migrationBuilder.CreateTable(
                name: "assistant_proposal_decisions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    proposal_id = table.Column<long>(type: "bigint", nullable: false),
                    decision = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    actor = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    auto = table.Column<bool>(type: "boolean", nullable: false),
                    settings_id = table.Column<long>(type: "bigint", nullable: true),
                    snapshot_hash = table.Column<string>(type: "character(64)", nullable: true),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    result_ref = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assistant_proposal_decisions", x => x.id);
                    table.CheckConstraint("ck_assistant_proposal_decisions_auto", "(actor = 'system:auto-accept') = auto AND (NOT auto OR (decision = 'accepted' AND settings_id IS NOT NULL AND snapshot_hash IS NOT NULL))");
                    table.CheckConstraint("ck_assistant_proposal_decisions_decision", "decision IN ('accepted', 'rejected')");
                    table.CheckConstraint("ck_assistant_proposal_decisions_result", "decision <> 'accepted' OR result_ref IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_assistant_proposal_decisions_assistant_proposals_proposal_id",
                        column: x => x.proposal_id,
                        principalSchema: "workflow",
                        principalTable: "assistant_proposals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assistant_proposal_decisions_decided_at",
                schema: "workflow",
                table: "assistant_proposal_decisions",
                column: "decided_at");

            migrationBuilder.CreateIndex(
                name: "ux_assistant_proposal_decisions_proposal_id",
                schema: "workflow",
                table: "assistant_proposal_decisions",
                column: "proposal_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assistant_proposals_created_at",
                schema: "workflow",
                table: "assistant_proposals",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ux_assistant_proposals_dedupe_key_created_on",
                schema: "workflow",
                table: "assistant_proposals",
                columns: new[] { "dedupe_key", "created_on" },
                unique: true);

            // Insert-only for the runtime role (spec P9-08 AC 11): the default privileges give it DML on new tables; take UPDATE and DELETE away.
            migrationBuilder.MakeAppendOnlyForAppRole("workflow", "assistant_proposals");
            migrationBuilder.MakeAppendOnlyForAppRole("workflow", "assistant_proposal_decisions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assistant_proposal_decisions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "assistant_proposals",
                schema: "workflow");
        }
    }
}
