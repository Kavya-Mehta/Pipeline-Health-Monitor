using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PipelineHealthMonitor.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pipelines",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    source_system = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    sink_system = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipelines", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pipeline_runs",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    pipeline_id = table.Column<int>(type: "int", nullable: false),
                    started_at = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ended_at = table.Column<DateTime>(type: "datetime2", nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    records_read = table.Column<int>(type: "int", nullable: false),
                    records_written = table.Column<int>(type: "int", nullable: false),
                    records_failed = table.Column<int>(type: "int", nullable: false),
                    triggered_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pipeline_runs", x => x.id);
                    table.CheckConstraint("CK_pipeline_runs_status", "status IN ('RUNNING', 'SUCCESS', 'FAILED', 'PARTIAL')");
                    table.ForeignKey(
                        name: "FK_pipeline_runs_pipelines_pipeline_id",
                        column: x => x.pipeline_id,
                        principalTable: "pipelines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "run_errors",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    run_id = table.Column<int>(type: "int", nullable: false),
                    error_code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    error_message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    severity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    occurred_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_run_errors", x => x.id);
                    table.CheckConstraint("CK_run_errors_severity", "severity IN ('INFO', 'WARNING', 'CRITICAL')");
                    table.ForeignKey(
                        name: "FK_run_errors_pipeline_runs_run_id",
                        column: x => x.run_id,
                        principalTable: "pipeline_runs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pipeline_runs_pipeline_id",
                table: "pipeline_runs",
                column: "pipeline_id");

            migrationBuilder.CreateIndex(
                name: "IX_run_errors_run_id",
                table: "run_errors",
                column: "run_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "run_errors");

            migrationBuilder.DropTable(
                name: "pipeline_runs");

            migrationBuilder.DropTable(
                name: "pipelines");
        }
    }
}
