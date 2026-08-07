using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReprocessModesAndTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AnalysisStartedAt",
                table: "Repositories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "Repositories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeAiAnalysis",
                table: "Repositories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IncludeStaticAnalysis",
                table: "Repositories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReprocessMode",
                table: "Repositories",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnalysisStartedAt",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "IncludeAiAnalysis",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "IncludeStaticAnalysis",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "ReprocessMode",
                table: "Repositories");
        }
    }
}
