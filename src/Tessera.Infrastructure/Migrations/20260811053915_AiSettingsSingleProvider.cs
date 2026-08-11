using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tessera.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AiSettingsSingleProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "FallbackProviderName",
                table: "AiSettings",
                newName: "EmbeddingModel");

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingEndpoint",
                table: "AiSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Endpoint",
                table: "AiSettings",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddingEndpoint",
                table: "AiSettings");

            migrationBuilder.DropColumn(
                name: "Endpoint",
                table: "AiSettings");

            migrationBuilder.RenameColumn(
                name: "EmbeddingModel",
                table: "AiSettings",
                newName: "FallbackProviderName");
        }
    }
}
