using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <summary>ADR 0292 (Q1): the eval baseline may also be accepted by the auto option, as <c>auto:xd</c> with the setting snapshot as its reason. <c>Down</c> fails while an auto row exists (the history is not rewritten).</summary>
    public partial class AllowAutoEvalBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_eval_baselines_actor",
                schema: "ai",
                table: "eval_baselines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_eval_baselines_actor",
                schema: "ai",
                table: "eval_baselines",
                sql: "accepted_by = 'cli:xd' OR (accepted_by = 'auto:xd' AND COALESCE(reason, '') LIKE 'auto: %')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ai_eval_baselines_actor",
                schema: "ai",
                table: "eval_baselines");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ai_eval_baselines_actor",
                schema: "ai",
                table: "eval_baselines",
                sql: "accepted_by = 'cli:xd'");
        }
    }
}
