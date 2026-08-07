using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalysisProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "Repositories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedCount",
                table: "Repositories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "StageStartedAt",
                table: "Repositories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalCount",
                table: "Repositories",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Status_UpdatedAt",
                table: "Repositories",
                columns: new[] { "Status", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Repositories_Status_UpdatedAt",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "ProcessedCount",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "StageStartedAt",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "TotalCount",
                table: "Repositories");
        }
    }
}
