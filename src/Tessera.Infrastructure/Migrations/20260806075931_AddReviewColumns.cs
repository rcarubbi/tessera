using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EditedAt",
                table: "KnowledgeNodes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EditedBy",
                table: "KnowledgeNodes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "KnowledgeNodes");

            migrationBuilder.DropColumn(
                name: "EditedBy",
                table: "KnowledgeNodes");
        }
    }
}
