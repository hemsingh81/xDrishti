using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace XDrishti.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "service_runs",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    trigger = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    attempt = table.Column<short>(type: "smallint", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    data_job_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_runs", x => x.id);
                    table.CheckConstraint("ck_service_runs_attempt", "attempt >= 1");
                    table.CheckConstraint("ck_service_runs_status", "status IN ('running', 'succeeded', 'failed', 'skipped')");
                    table.CheckConstraint("ck_service_runs_trigger", "trigger IN ('scheduled', 'catch_up', 'after', 'manual')");
                });

            migrationBuilder.CreateTable(
                name: "service_schedules",
                schema: "platform",
                columns: table => new
                {
                    service_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    schedule = table.Column<string>(type: "jsonb", nullable: false),
                    retries = table.Column<short>(type: "smallint", nullable: false),
                    timeout_minutes = table.Column<short>(type: "smallint", nullable: false),
                    catch_up = table.Column<bool>(type: "boolean", nullable: false),
                    notify_on_failure = table.Column<bool>(type: "boolean", nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    updated_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_schedules", x => x.service_id);
                    table.CheckConstraint("ck_service_schedules_retries", "retries BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_service_schedules_schedule", "schedule ->> 'type' IN ('daily', 'weekly', 'after', 'window', 'interval', 'continuous')");
                    table.CheckConstraint("ck_service_schedules_timeout", "timeout_minutes BETWEEN 1 AND 240");
                });

            migrationBuilder.CreateIndex(
                name: "ix_service_runs_service_id_started_at",
                schema: "platform",
                table: "service_runs",
                columns: new[] { "service_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_service_runs_one_running",
                schema: "platform",
                table: "service_runs",
                column: "service_id",
                unique: true,
                filter: "status = 'running'");

            // The schedules and the run history are kept for good: the runtime role reads, inserts and updates them (a save, a run that ends) and can never delete.
            migrationBuilder.GrantTableToAppRole(Schemas.Platform, "service_schedules");
            migrationBuilder.GrantTableToAppRole(Schemas.Platform, "service_runs");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Platform, "service_schedules");
            migrationBuilder.NeverDeleteForAppRole(Schemas.Platform, "service_runs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "service_runs",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "service_schedules",
                schema: "platform");
        }
    }
}
